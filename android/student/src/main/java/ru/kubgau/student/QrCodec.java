package ru.kubgau.student;

import android.graphics.Bitmap;

import com.google.zxing.BarcodeFormat;
import com.google.zxing.EncodeHintType;
import com.google.zxing.qrcode.QRCodeWriter;
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel;

import java.nio.charset.StandardCharsets;
import java.util.EnumMap;
import java.util.Map;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public final class QrCodec {
    private QrCodec() {
    }

    public static long currentWindow() {
        return System.currentTimeMillis() / 1000L / 5L;
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

    public static String studentPayload(String login, String password, String device) {
        long window = currentWindow();
        return "KG1|S|" + login + "|" + password + "|" + device + "|" + window + "|" + sign(login, password, device, window);
    }

    public static Bitmap qrBitmap(String text, int size) {
        try {
            Map<EncodeHintType, Object> hints = new EnumMap<>(EncodeHintType.class);
            hints.put(EncodeHintType.CHARACTER_SET, "UTF-8");
            hints.put(EncodeHintType.MARGIN, 1);
            hints.put(EncodeHintType.ERROR_CORRECTION, ErrorCorrectionLevel.M);
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
}
