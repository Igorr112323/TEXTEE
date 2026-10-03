package ru.kubgau.teacher;

import android.graphics.Bitmap;
import android.util.Base64;

import com.google.zxing.BarcodeFormat;
import com.google.zxing.BinaryBitmap;
import com.google.zxing.DecodeHintType;
import com.google.zxing.EncodeHintType;
import com.google.zxing.MultiFormatReader;
import com.google.zxing.RGBLuminanceSource;
import com.google.zxing.common.HybridBinarizer;
import com.google.zxing.qrcode.QRCodeWriter;
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel;

import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.zip.DataFormatException;
import java.util.zip.Deflater;
import java.util.zip.Inflater;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public final class QrCodec {
    public static final int WINDOW_SLACK = 9;

    private QrCodec() {
    }

    public static final class Ticket {
        public final String login;
        public final String password;
        public final String device;
        public final long window;
        public final String sig;

        Ticket(String login, String password, String device, long window, String sig) {
            this.login = login;
            this.password = password;
            this.device = device;
            this.window = window;
            this.sig = sig;
        }
    }

    public static final class Frame {
        public final char kind;
        public final String rollId;
        public final int index;
        public final int count;
        public final String chunk;

        Frame(char kind, String rollId, int index, int count, String chunk) {
            this.kind = kind;
            this.rollId = rollId;
            this.index = index;
            this.count = count;
            this.chunk = chunk;
        }
    }

    public static final class Person {
        public final int id;
        public final String login;
        public final String hash;
        public final String name;
        public String device;

        Person(int id, String login, String hash, String name, String device) {
            this.id = id;
            this.login = login;
            this.hash = hash;
            this.name = name;
            this.device = device;
        }
    }

    public static long currentWindow() {
        return System.currentTimeMillis() / 1000L / 5L;
    }

    public static boolean fresh(long window) {
        long delta = Math.abs(currentWindow() - window);
        return delta <= WINDOW_SLACK;
    }

    public static String passwordHash(String password) {
        try {
            byte[] raw = MessageDigest.getInstance("SHA-256").digest(password.getBytes(StandardCharsets.UTF_8));
            return hex(raw, 8);
        } catch (Exception e) {
            return "";
        }
    }

    public static String sign(String login, String password, String device, long window) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(password.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            byte[] raw = mac.doFinal((login + "\n" + device + "\n" + window).getBytes(StandardCharsets.UTF_8));
            return hex(raw, 4);
        } catch (Exception e) {
            return "";
        }
    }

    public static boolean signatureValid(Ticket ticket) {
        return sign(ticket.login, ticket.password, ticket.device, ticket.window).equals(ticket.sig);
    }

    public static Ticket parseStudent(String text) {
        if (text == null) return null;
        String[] parts = text.trim().split("\\|", -1);
        if (parts.length != 7 || !"KG1".equals(parts[0]) || !"S".equals(parts[1])) return null;
        if (parts[2].isEmpty() || parts[3].isEmpty() || parts[4].isEmpty() || parts[6].isEmpty()) return null;
        long window;
        try {
            window = Long.parseLong(parts[5]);
        } catch (NumberFormatException e) {
            return null;
        }
        return new Ticket(parts[2], parts[3], parts[4], window, parts[6].toLowerCase());
    }

    public static Frame parseFrame(String text) {
        if (text == null) return null;
        String[] parts = text.trim().split("\\|", 6);
        if (parts.length < 6 || !"KG1".equals(parts[0]) || parts[1].length() != 1) return null;
        char kind = parts[1].charAt(0);
        if (kind != 'C' && kind != 'R') return null;
        int index;
        int count;
        try {
            index = Integer.parseInt(parts[3]);
            count = Integer.parseInt(parts[4]);
        } catch (NumberFormatException e) {
            return null;
        }
        if (index < 1 || count < 1 || index > count || count > 80 || parts[2].isEmpty()) return null;
        return new Frame(kind, parts[2].toLowerCase(), index, count, parts[5]);
    }

    public static List<String> encodeResult(String rollId, String body) {
        List<String> single = encodeFrames('R', rollId, body, 100000);
        if (single.size() == 1 && single.get(0).length() <= 1400) return single;
        return encodeFrames('R', rollId, body, 320);
    }

    public static List<String> encodeFrames(char kind, String rollId, String body, int chunkSize) {
        String b64 = Base64.encodeToString(compress(body), Base64.NO_WRAP);
        List<String> chunks = new ArrayList<>();
        if (b64.isEmpty()) {
            chunks.add("");
        } else {
            for (int i = 0; i < b64.length(); i += chunkSize) {
                chunks.add(b64.substring(i, Math.min(b64.length(), i + chunkSize)));
            }
        }
        List<String> frames = new ArrayList<>();
        for (int i = 0; i < chunks.size(); i++) {
            frames.add("KG1|" + kind + "|" + rollId + "|" + (i + 1) + "|" + chunks.size() + "|" + chunks.get(i));
        }
        return frames;
    }

    public static List<Person> parseRoster(String body) {
        List<Person> list = new ArrayList<>();
        for (String line : body.split("\n", -1)) {
            if (line.isEmpty()) continue;
            String[] parts = line.split("\t", -1);
            if (parts.length < 5) continue;
            int id;
            try {
                id = Integer.parseInt(parts[0]);
            } catch (NumberFormatException e) {
                continue;
            }
            list.add(new Person(id, parts[1], parts[2], parts[3], parts[4]));
        }
        return list;
    }

    public static final class Collector {
        private String roll;
        private char kind;
        private int count;
        private final Map<Integer, String> chunks = new HashMap<>();

        public int got() {
            return chunks.size();
        }

        public int count() {
            return count;
        }

        public String add(Frame frame) {
            if (roll == null || !roll.equals(frame.rollId) || kind != frame.kind || count != frame.count) {
                roll = frame.rollId;
                kind = frame.kind;
                count = frame.count;
                chunks.clear();
            }
            chunks.put(frame.index, frame.chunk);
            if (chunks.size() < count) return null;
            StringBuilder joined = new StringBuilder();
            for (int i = 1; i <= count; i++) {
                String chunk = chunks.get(i);
                if (chunk == null) return null;
                joined.append(chunk);
            }
            try {
                byte[] raw = Base64.decode(joined.toString(), Base64.NO_WRAP);
                byte[] text = decompress(raw);
                if (text == null) return null;
                return new String(text, StandardCharsets.UTF_8);
            } catch (IllegalArgumentException e) {
                return null;
            }
        }
    }

    public static Bitmap qrBitmap(String text, int size) {
        try {
            Map<EncodeHintType, Object> hints = new EnumMap<>(EncodeHintType.class);
            hints.put(EncodeHintType.CHARACTER_SET, "UTF-8");
            hints.put(EncodeHintType.MARGIN, 1);
            hints.put(EncodeHintType.ERROR_CORRECTION, text.length() > 700 ? ErrorCorrectionLevel.L : ErrorCorrectionLevel.M);
            var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, size, size, hints);
            Bitmap bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888);
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    bitmap.setPixel(x, y, matrix.get(x, y) ? 0xFF000000 : 0xFFFFFFFF);
                }
            }
            return bitmap;
        } catch (Exception e) {
            return null;
        }
    }

    public static String decode(Bitmap bitmap) {
        if (bitmap == null) return null;
        int width = bitmap.getWidth();
        int height = bitmap.getHeight();
        int[] pixels = new int[width * height];
        bitmap.getPixels(pixels, 0, width, 0, 0, width, height);
        Map<DecodeHintType, Object> hints = new EnumMap<>(DecodeHintType.class);
        hints.put(DecodeHintType.POSSIBLE_FORMATS, List.of(BarcodeFormat.QR_CODE));
        hints.put(DecodeHintType.TRY_HARDER, Boolean.TRUE);
        hints.put(DecodeHintType.CHARACTER_SET, "UTF-8");
        MultiFormatReader reader = new MultiFormatReader();
        reader.setHints(hints);
        try {
            return reader.decodeWithState(new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(width, height, pixels)))).getText();
        } catch (Exception e) {
            return null;
        } finally {
            reader.reset();
        }
    }

    private static String hex(byte[] data, int n) {
        char[] alphabet = "0123456789abcdef".toCharArray();
        char[] out = new char[n * 2];
        for (int i = 0; i < n; i++) {
            int value = data[i] & 0xff;
            out[i * 2] = alphabet[value >>> 4];
            out[i * 2 + 1] = alphabet[value & 0x0f];
        }
        return new String(out);
    }

    private static byte[] compress(String text) {
        byte[] input = text.getBytes(StandardCharsets.UTF_8);
        Deflater deflater = new Deflater(Deflater.BEST_COMPRESSION, false);
        deflater.setInput(input);
        deflater.finish();
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[1024];
        while (!deflater.finished()) {
            int n = deflater.deflate(buf);
            if (n == 0) break;
            out.write(buf, 0, n);
        }
        deflater.end();
        return out.toByteArray();
    }

    private static byte[] decompress(byte[] input) {
        Inflater inflater = new Inflater(false);
        inflater.setInput(input);
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[1024];
        try {
            while (!inflater.finished()) {
                int n = inflater.inflate(buf);
                if (n == 0) break;
                out.write(buf, 0, n);
            }
        } catch (DataFormatException e) {
            return null;
        } finally {
            inflater.end();
        }
        return out.toByteArray();
    }
}
