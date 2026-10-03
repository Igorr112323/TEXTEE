package ru.kubgau.teacher;

import android.Manifest;
import android.app.Activity;
import android.content.ContentValues;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.graphics.Matrix;
import android.graphics.SurfaceTexture;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.provider.MediaStore;
import android.util.Size;
import android.view.Surface;
import android.view.TextureView;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.ImageView;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

public final class MainActivity extends Activity {
    private static final int MODE_IDLE = 0;
    private static final int MODE_READY = 1;
    private static final int MODE_SCAN_SESSION = 2;
    private static final int MODE_SCAN_STUDENTS = 3;
    private static final int MODE_TRANSFER = 4;
    private static final int COLOR_RED = 0xFFB34A4A;
    private static final int COLOR_GREEN = 0xFF2E7D32;
    private static final int COLOR_GRAY = 0xFF8A8A8A;

    private int mode = MODE_IDLE;
    private String rollId = "";
    private final Map<String, QrCodec.Person> byLogin = new LinkedHashMap<>();
    private final Map<String, QrCodec.Person> byDevice = new LinkedHashMap<>();
    private final Map<Integer, String> present = new LinkedHashMap<>();
    private final List<int[]> suspiciousIds = new ArrayList<>();
    private final List<String> suspiciousNames = new ArrayList<>();
    private final QrCodec.Collector sessionCollector = new QrCodec.Collector();
    private List<String> resultFrames = new ArrayList<>();
    private int resultIndex;
    private Bitmap resultBitmap;
    private String lastText = "";
    private long lastTextAt;

    private View homeLayer;
    private View cameraLayer;
    private View transferLayer;
    private ImageView logo;
    private TextView groupLine;
    private TextView countLine;
    private TextView listText;
    private TextView statusText;
    private TextView cameraStatus;
    private TextView flashName;
    private TextView transferLabel;
    private ImageView transferQr;
    private Button primaryButton;
    private Button secondaryButton;
    private Button tertiaryButton;
    private TextureView textureView;
    private ScrollView listScroll;
    private View spacer;

    private CameraDevice cameraDevice;
    private CameraCaptureSession captureSession;
    private HandlerThread cameraThread;
    private Handler cameraHandler;
    private Size previewSize;
    private int sensorOrientation = 90;
    private boolean scanning;

    private final Handler ui = new Handler();
    private final Runnable transferTick = new Runnable() {
        @Override
        public void run() {
            if (mode != MODE_TRANSFER || resultFrames.isEmpty()) return;
            showResultFrame();
            resultIndex = (resultIndex + 1) % resultFrames.size();
            ui.postDelayed(this, 700);
        }
    };
    private final Runnable scanTick = new Runnable() {
        @Override
        public void run() {
            if (!scanning) return;
            Bitmap bitmap = textureView.getBitmap(720, 960);
            String text = QrCodec.decode(bitmap);
            if (bitmap != null) bitmap.recycle();
            if (text != null) onQr(text);
            ui.postDelayed(this, 180);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        homeLayer = findViewById(R.id.homeLayer);
        cameraLayer = findViewById(R.id.cameraLayer);
        transferLayer = findViewById(R.id.transferLayer);
        logo = findViewById(R.id.logo);
        groupLine = findViewById(R.id.groupLine);
        countLine = findViewById(R.id.countLine);
        listText = findViewById(R.id.listText);
        statusText = findViewById(R.id.statusText);
        cameraStatus = findViewById(R.id.cameraStatus);
        flashName = findViewById(R.id.flashName);
        transferLabel = findViewById(R.id.transferLabel);
        transferQr = findViewById(R.id.transferQr);
        primaryButton = findViewById(R.id.primaryButton);
        secondaryButton = findViewById(R.id.secondaryButton);
        tertiaryButton = findViewById(R.id.tertiaryButton);
        textureView = findViewById(R.id.textureView);
        listScroll = findViewById(R.id.listScroll);
        spacer = findViewById(R.id.spacer);
        primaryButton.setOnClickListener(v -> onPrimary());
        secondaryButton.setOnClickListener(v -> onSecondary());
        tertiaryButton.setOnClickListener(v -> onTertiary());
        findViewById(R.id.cameraCancel).setOnClickListener(v -> leaveCamera());
        findViewById(R.id.saveButton).setOnClickListener(v -> saveFile());
        findViewById(R.id.transferBack).setOnClickListener(v -> showReady());
        textureView.setSurfaceTextureListener(new TextureView.SurfaceTextureListener() {
            @Override
            public void onSurfaceTextureAvailable(SurfaceTexture surface, int width, int height) {
                if (scanning) openCamera();
            }

            @Override
            public void onSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height) {
                configureTransform();
            }

            @Override
            public boolean onSurfaceTextureDestroyed(SurfaceTexture surface) {
                return true;
            }

            @Override
            public void onSurfaceTextureUpdated(SurfaceTexture surface) {
            }
        });
        showIdle();
    }

    @Override
    protected void onDestroy() {
        stopCamera();
        ui.removeCallbacksAndMessages(null);
        super.onDestroy();
    }

    @Override
    public void onBackPressed() {
        if (mode == MODE_SCAN_SESSION || mode == MODE_SCAN_STUDENTS) {
            leaveCamera();
            return;
        }
        if (mode == MODE_TRANSFER) {
            showReady();
            return;
        }
        super.onBackPressed();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 7 && grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
            beginScan(mode == MODE_READY || mode == MODE_SCAN_STUDENTS ? MODE_SCAN_STUDENTS : MODE_SCAN_SESSION);
        } else if (requestCode == 7) {
            setStatus("Разрешите камеру, чтобы сканировать QR", COLOR_RED);
        }
    }

    private void onPrimary() {
        if (mode == MODE_IDLE || mode == MODE_READY && byLogin.isEmpty()) {
            beginScan(MODE_SCAN_SESSION);
            return;
        }
        if (mode == MODE_READY) beginScan(MODE_SCAN_STUDENTS);
    }

    private void onSecondary() {
        if (mode == MODE_READY && !byLogin.isEmpty()) beginScan(MODE_SCAN_SESSION);
        else beginScan(MODE_SCAN_SESSION);
    }

    private void onTertiary() {
        if (present.isEmpty() && suspiciousIds.isEmpty()) {
            setStatus("Сначала отметьте студентов", COLOR_RED);
            return;
        }
        showTransfer();
    }

    private void showIdle() {
        mode = MODE_IDLE;
        stopCamera();
        ui.removeCallbacks(transferTick);
        homeLayer.setVisibility(View.VISIBLE);
        cameraLayer.setVisibility(View.GONE);
        transferLayer.setVisibility(View.GONE);
        logo.getLayoutParams().height = dp(240);
        logo.requestLayout();
        groupLine.setVisibility(View.GONE);
        countLine.setVisibility(View.GONE);
        listScroll.setVisibility(View.GONE);
        spacer.setVisibility(View.VISIBLE);
        primaryButton.setText("Сканировать сессию с ПК");
        primaryButton.setBackgroundResource(R.drawable.button_primary);
        secondaryButton.setVisibility(View.GONE);
        tertiaryButton.setVisibility(View.GONE);
        setStatus("Наведите камеру на код сессии на экране ПК. Сеть не нужна.", COLOR_GRAY);
    }

    private void showReady() {
        mode = MODE_READY;
        stopCamera();
        ui.removeCallbacks(transferTick);
        homeLayer.setVisibility(View.VISIBLE);
        cameraLayer.setVisibility(View.GONE);
        transferLayer.setVisibility(View.GONE);
        logo.getLayoutParams().height = dp(96);
        logo.requestLayout();
        groupLine.setVisibility(View.VISIBLE);
        groupLine.setText(groupTitle());
        countLine.setVisibility(View.VISIBLE);
        countLine.setText(present.size() + " / " + byLogin.size());
        listScroll.setVisibility(View.VISIBLE);
        spacer.setVisibility(View.GONE);
        listText.setText(listBody());
        primaryButton.setText("Сканировать студентов");
        primaryButton.setBackgroundResource(R.drawable.button_green);
        secondaryButton.setVisibility(View.VISIBLE);
        secondaryButton.setText("Новая сессия");
        tertiaryButton.setVisibility(View.VISIBLE);
        tertiaryButton.setText("Передать на ПК");
        setStatus("Группа считана. Сканируйте QR студентов.", COLOR_GREEN);
    }

    private void beginScan(int next) {
        if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
            mode = next;
            requestPermissions(new String[]{Manifest.permission.CAMERA}, 7);
            return;
        }
        mode = next;
        homeLayer.setVisibility(View.GONE);
        transferLayer.setVisibility(View.GONE);
        cameraLayer.setVisibility(View.VISIBLE);
        flashName.setVisibility(View.GONE);
        cameraStatus.setText(next == MODE_SCAN_SESSION
                ? "Держите камеру на коде сессии"
                : "Наведите на QR студента");
        cameraStatus.setTextColor(COLOR_GRAY);
        scanning = true;
        if (textureView.isAvailable()) openCamera();
        ui.removeCallbacks(scanTick);
        ui.postDelayed(scanTick, 300);
    }

    private void leaveCamera() {
        stopCamera();
        if (byLogin.isEmpty()) showIdle();
        else showReady();
    }

    private void showTransfer() {
        StringBuilder body = new StringBuilder();
        for (Map.Entry<Integer, String> entry : present.entrySet()) {
            body.append('P').append('\t').append(entry.getKey()).append('\t').append(entry.getValue()).append('\n');
        }
        for (int i = 0; i < suspiciousIds.size(); i++) {
            body.append('S').append('\t').append(suspiciousIds.get(i)[0]).append('\t').append(suspiciousNames.get(i)).append('\n');
        }
        resultFrames = QrCodec.encodeResult(rollId, body.toString());
        resultIndex = 0;
        mode = MODE_TRANSFER;
        stopCamera();
        homeLayer.setVisibility(View.GONE);
        cameraLayer.setVisibility(View.GONE);
        transferLayer.setVisibility(View.VISIBLE);
        showResultFrame();
        ui.removeCallbacks(transferTick);
        if (resultFrames.size() > 1) ui.postDelayed(transferTick, 700);
    }

    private void showResultFrame() {
        String frame = resultFrames.get(resultIndex);
        Bitmap bitmap = QrCodec.qrBitmap(frame, 900);
        transferQr.setImageBitmap(bitmap);
        if (resultBitmap != null && resultBitmap != bitmap) resultBitmap.recycle();
        resultBitmap = bitmap;
        transferLabel.setText(resultFrames.size() == 1
                ? "Покажите этот код камере ПК"
                : "Кадр " + (resultIndex + 1) + " из " + resultFrames.size() + ". Держите телефон перед камерой ПК.");
    }

    private void onQr(String text) {
        long now = android.os.SystemClock.elapsedRealtime();
        if (text.equals(lastText) && now - lastTextAt < 1600) return;
        lastText = text;
        lastTextAt = now;
        if (mode == MODE_SCAN_SESSION) onSessionQr(text);
        else if (mode == MODE_SCAN_STUDENTS) onStudentQr(text);
    }

    private void onSessionQr(String text) {
        QrCodec.Frame frame = QrCodec.parseFrame(text);
        if (frame == null || frame.kind != 'C') {
            cameraStatus.setText("Это не код сессии. Наведите на экран ПК.");
            cameraStatus.setTextColor(COLOR_RED);
            return;
        }
        String body = sessionCollector.add(frame);
        if (body == null) {
            cameraStatus.setText("Сессия " + sessionCollector.got() + " / " + sessionCollector.count());
            cameraStatus.setTextColor(COLOR_GRAY);
            return;
        }
        byLogin.clear();
        byDevice.clear();
        present.clear();
        suspiciousIds.clear();
        suspiciousNames.clear();
        rollId = frame.rollId;
        for (QrCodec.Person person : QrCodec.parseRoster(body)) {
            byLogin.put(person.login.toLowerCase(Locale.ROOT), person);
            if (person.device != null && !person.device.isEmpty()) byDevice.put(person.device, person);
        }
        if (byLogin.isEmpty()) {
            cameraStatus.setText("В коде сессии нет студентов");
            cameraStatus.setTextColor(COLOR_RED);
            return;
        }
        pulse();
        showReady();
    }

    private void onStudentQr(String text) {
        QrCodec.Ticket ticket = QrCodec.parseStudent(text);
        if (ticket == null) {
            fail("Не тот код");
            return;
        }
        if (!QrCodec.fresh(ticket.window)) {
            fail("Код устарел — пусть студент обновит экран");
            return;
        }
        if (!QrCodec.signatureValid(ticket)) {
            fail("Не получилось, попробуйте ещё раз");
            return;
        }
        QrCodec.Person person = byLogin.get(ticket.login.toLowerCase(Locale.ROOT));
        if (person == null) {
            fail("Студент не из этой группы");
            return;
        }
        if (!QrCodec.passwordHash(ticket.password).equals(person.hash)) {
            fail("НЕВЕРНЫЙ ЛОГИН ИЛИ ПАРОЛЬ");
            return;
        }
        if (person.device != null && !person.device.isEmpty() && !person.device.equals(ticket.device)) {
            fail("Аккаунт привязан к другому телефону");
            return;
        }
        QrCodec.Person owner = byDevice.get(ticket.device);
        if (owner != null && owner.id != person.id) {
            suspiciousIds.add(new int[]{owner.id});
            suspiciousNames.add(ticket.login);
            fail("Что-то не так");
            flash(owner.name, COLOR_RED);
            return;
        }
        if (present.containsKey(person.id)) {
            cameraStatus.setText("Уже отмечен: " + person.name);
            cameraStatus.setTextColor(COLOR_GREEN);
            return;
        }
        present.put(person.id, ticket.device);
        if (person.device == null || person.device.isEmpty()) {
            person.device = ticket.device;
            byDevice.put(ticket.device, person);
        }
        pulse();
        cameraStatus.setText("Отмечен: " + person.name + "  ·  " + present.size() + " / " + byLogin.size());
        cameraStatus.setTextColor(COLOR_GREEN);
        flash(person.name, COLOR_GREEN);
    }

    private void fail(String message) {
        cameraStatus.setText(message);
        cameraStatus.setTextColor(COLOR_RED);
        buzz();
    }

    private void flash(String name, int color) {
        flashName.setText(name);
        flashName.setTextColor(color);
        flashName.setVisibility(View.VISIBLE);
        ui.postDelayed(() -> flashName.setVisibility(View.GONE), 1200);
    }

    private String groupTitle() {
        return "Сессия " + rollId;
    }

    private String listBody() {
        StringBuilder builder = new StringBuilder();
        for (QrCodec.Person person : byLogin.values()) {
            boolean marked = present.containsKey(person.id);
            builder.append(marked ? "●  " : "○  ").append(person.name).append('\n');
        }
        return builder.toString().trim();
    }

    private void setStatus(String text, int color) {
        statusText.setText(text);
        statusText.setTextColor(color);
    }

    private void saveFile() {
        if (resultFrames.isEmpty()) return;
        StringBuilder builder = new StringBuilder();
        for (String frame : resultFrames) builder.append(frame).append('\n');
        byte[] bytes = builder.toString().getBytes(StandardCharsets.UTF_8);
        try {
            if (Build.VERSION.SDK_INT >= 29) {
                ContentValues values = new ContentValues();
                values.put(MediaStore.Downloads.DISPLAY_NAME, "KubGAU-rollcall.kjournal");
                values.put(MediaStore.Downloads.MIME_TYPE, "text/plain");
                var uri = getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                if (uri == null) throw new IllegalStateException("downloads");
                try (OutputStream out = getContentResolver().openOutputStream(uri)) {
                    if (out == null) throw new IllegalStateException("stream");
                    out.write(bytes);
                }
            } else {
                File dir = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS);
                if (!dir.exists() && !dir.mkdirs()) throw new IllegalStateException("mkdir");
                try (FileOutputStream out = new FileOutputStream(new File(dir, "KubGAU-rollcall.kjournal"))) {
                    out.write(bytes);
                }
            }
            transferLabel.setText("Файл сохранён в Загрузки: KubGAU-rollcall.kjournal");
        } catch (Exception e) {
            transferLabel.setText("Не удалось сохранить файл");
        }
    }

    private void openCamera() {
        closeCameraDevice();
        CameraManager manager = (CameraManager) getSystemService(CAMERA_SERVICE);
        if (manager == null) return;
        try {
            String chosen = null;
            for (String id : manager.getCameraIdList()) {
                CameraCharacteristics characteristics = manager.getCameraCharacteristics(id);
                Integer facing = characteristics.get(CameraCharacteristics.LENS_FACING);
                if (facing != null && facing == CameraCharacteristics.LENS_FACING_BACK) {
                    chosen = id;
                    Integer orientation = characteristics.get(CameraCharacteristics.SENSOR_ORIENTATION);
                    if (orientation != null) sensorOrientation = orientation;
                    var map = characteristics.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
                    if (map != null) previewSize = chooseSize(map.getOutputSizes(SurfaceTexture.class));
                    break;
                }
            }
            if (chosen == null) {
                cameraStatus.setText("Камера не найдена");
                cameraStatus.setTextColor(COLOR_RED);
                return;
            }
            if (cameraThread == null) {
                cameraThread = new HandlerThread("camera");
                cameraThread.start();
                cameraHandler = new Handler(cameraThread.getLooper());
            }
            manager.openCamera(chosen, new CameraDevice.StateCallback() {
                @Override
                public void onOpened(CameraDevice camera) {
                    cameraDevice = camera;
                    startPreview();
                }

                @Override
                public void onDisconnected(CameraDevice camera) {
                    camera.close();
                    cameraDevice = null;
                }

                @Override
                public void onError(CameraDevice camera, int error) {
                    camera.close();
                    cameraDevice = null;
                }
            }, cameraHandler);
        } catch (CameraAccessException | SecurityException e) {
            cameraStatus.setText("Не удалось открыть камеру");
            cameraStatus.setTextColor(COLOR_RED);
        }
    }

    private void startPreview() {
        if (cameraDevice == null || !textureView.isAvailable() || previewSize == null) return;
        try {
            SurfaceTexture texture = textureView.getSurfaceTexture();
            texture.setDefaultBufferSize(previewSize.getWidth(), previewSize.getHeight());
            Surface surface = new Surface(texture);
            cameraDevice.createCaptureSession(Collections.singletonList(surface), new CameraCaptureSession.StateCallback() {
                @Override
                public void onConfigured(CameraCaptureSession session) {
                    captureSession = session;
                    try {
                        CaptureRequest.Builder builder = cameraDevice.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
                        builder.addTarget(surface);
                        builder.set(CaptureRequest.CONTROL_AF_MODE, CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_PICTURE);
                        session.setRepeatingRequest(builder.build(), null, cameraHandler);
                        ui.post(MainActivity.this::configureTransform);
                    } catch (Exception ignored) {
                    }
                }

                @Override
                public void onConfigureFailed(CameraCaptureSession session) {
                }
            }, cameraHandler);
        } catch (Exception ignored) {
        }
    }

    private void configureTransform() {
        if (previewSize == null || textureView.getWidth() == 0) return;
        int width = textureView.getWidth();
        int height = textureView.getHeight();
        Matrix matrix = new Matrix();
        float cx = width / 2f;
        float cy = height / 2f;
        android.graphics.RectF viewRect = new android.graphics.RectF(0, 0, width, height);
        android.graphics.RectF bufferRect = new android.graphics.RectF(0, 0, previewSize.getHeight(), previewSize.getWidth());
        bufferRect.offset(cx - bufferRect.centerX(), cy - bufferRect.centerY());
        matrix.setRectToRect(viewRect, bufferRect, Matrix.ScaleToFit.FILL);
        float scale = Math.max((float) height / previewSize.getHeight(), (float) width / previewSize.getWidth());
        matrix.postScale(scale, scale, cx, cy);
        matrix.postRotate(sensorOrientation, cx, cy);
        textureView.setTransform(matrix);
    }

    private void stopCamera() {
        scanning = false;
        ui.removeCallbacks(scanTick);
        closeCameraDevice();
    }

    private void closeCameraDevice() {
        if (captureSession != null) {
            try { captureSession.close(); } catch (Exception ignored) {}
            captureSession = null;
        }
        if (cameraDevice != null) {
            cameraDevice.close();
            cameraDevice = null;
        }
    }

    private static Size chooseSize(Size[] sizes) {
        Size best = sizes[0];
        int bestScore = Integer.MAX_VALUE;
        for (Size size : sizes) {
            int score = Math.abs(size.getWidth() - 1280) + Math.abs(size.getHeight() - 720);
            if (score < bestScore) {
                best = size;
                bestScore = score;
            }
        }
        return best;
    }

    private void pulse() {
        Vibrator vibrator = (Vibrator) getSystemService(VIBRATOR_SERVICE);
        if (vibrator == null) return;
        if (Build.VERSION.SDK_INT >= 26) {
            vibrator.vibrate(VibrationEffect.createWaveform(new long[]{0, 90, 70, 90}, -1));
        } else {
            vibrator.vibrate(new long[]{0, 90, 70, 90}, -1);
        }
    }

    private void buzz() {
        Vibrator vibrator = (Vibrator) getSystemService(VIBRATOR_SERVICE);
        if (vibrator == null) return;
        if (Build.VERSION.SDK_INT >= 26) {
            vibrator.vibrate(VibrationEffect.createOneShot(40, VibrationEffect.DEFAULT_AMPLITUDE));
        } else {
            vibrator.vibrate(40);
        }
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }
}
