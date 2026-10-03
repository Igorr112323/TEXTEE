package ru.kubgau.student;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Bitmap;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.view.View;
import android.view.WindowManager;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.TextView;

import java.security.SecureRandom;

public final class MainActivity extends Activity {
    private SharedPreferences prefs;
    private String login = "";
    private String password = "";
    private String deviceId = "";
    private long shownWindow = -1;
    private Bitmap qrBitmap;

    private View loginPanel;
    private View qrBlock;
    private ImageView qrView;
    private TextView statusText;
    private TextView hintText;
    private TextView loginError;
    private TextView logoutButton;
    private EditText loginInput;
    private EditText passwordInput;

    private final Handler ui = new Handler(Looper.getMainLooper());
    private final Runnable refresher = new Runnable() {
        @Override
        public void run() {
            refreshQr();
            ui.postDelayed(this, 500);
        }
    };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        prefs = getSharedPreferences("kubgau", MODE_PRIVATE);
        loginPanel = findViewById(R.id.loginPanel);
        qrBlock = findViewById(R.id.qrBlock);
        qrView = findViewById(R.id.qrView);
        statusText = findViewById(R.id.statusText);
        hintText = findViewById(R.id.hintText);
        loginError = findViewById(R.id.loginError);
        logoutButton = findViewById(R.id.logoutButton);
        loginInput = findViewById(R.id.loginInput);
        passwordInput = findViewById(R.id.passwordInput);
        deviceId = deviceId();
        findViewById(R.id.loginButton).setOnClickListener(v -> submit());
        findViewById(R.id.logoutButton).setOnClickListener(v -> logout());
        login = prefs.getString("login", "");
        password = prefs.getString("password", "");
        if (login.isEmpty() || password.isEmpty()) showLogin();
        else showQr();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (!login.isEmpty()) {
            brighten();
            ui.removeCallbacks(refresher);
            ui.post(refresher);
        }
    }

    @Override
    protected void onPause() {
        ui.removeCallbacks(refresher);
        super.onPause();
    }

    private void submit() {
        String nextLogin = loginInput.getText().toString().trim();
        String nextPassword = passwordInput.getText().toString().trim();
        if (nextLogin.isEmpty() || nextPassword.isEmpty()) {
            loginError.setText("Введите логин и пароль");
            loginError.setVisibility(View.VISIBLE);
            return;
        }
        loginError.setVisibility(View.GONE);
        login = nextLogin;
        password = nextPassword;
        prefs.edit().putString("login", login).putString("password", password).apply();
        hideKeyboard();
        showQr();
    }

    private void logout() {
        login = "";
        password = "";
        shownWindow = -1;
        prefs.edit().remove("login").remove("password").apply();
        ui.removeCallbacks(refresher);
        showLogin();
    }

    private void showLogin() {
        loginPanel.setVisibility(View.VISIBLE);
        qrBlock.setVisibility(View.GONE);
        logoutButton.setVisibility(View.GONE);
        loginInput.setText("");
        passwordInput.setText("");
        WindowManager.LayoutParams params = getWindow().getAttributes();
        params.screenBrightness = WindowManager.LayoutParams.BRIGHTNESS_OVERRIDE_NONE;
        getWindow().setAttributes(params);
    }

    private void showQr() {
        loginPanel.setVisibility(View.GONE);
        qrBlock.setVisibility(View.VISIBLE);
        logoutButton.setVisibility(View.VISIBLE);
        statusText.setText("Покажите код камере преподавателя");
        hintText.setText("Код обновляется сам. Скриншот не сработает.");
        brighten();
        shownWindow = -1;
        refreshQr();
        ui.removeCallbacks(refresher);
        ui.post(refresher);
    }

    private void refreshQr() {
        if (login.isEmpty()) return;
        long window = QrCodec.currentWindow();
        if (window == shownWindow) return;
        shownWindow = window;
        String payload = QrCodec.studentPayload(login, password, deviceId);
        int size = Math.round(280 * getResources().getDisplayMetrics().density);
        Bitmap bitmap = QrCodec.qrBitmap(payload, Math.max(size, 640));
        qrView.setImageBitmap(bitmap);
        if (qrBitmap != null && qrBitmap != bitmap) qrBitmap.recycle();
        qrBitmap = bitmap;
    }

    private void brighten() {
        WindowManager.LayoutParams params = getWindow().getAttributes();
        params.screenBrightness = 1f;
        getWindow().setAttributes(params);
    }

    private String deviceId() {
        String stored = prefs.getString("device", "");
        if (stored != null && stored.length() >= 8) return stored;
        String androidId = Settings.Secure.getString(getContentResolver(), Settings.Secure.ANDROID_ID);
        if (androidId == null || androidId.length() < 8 || "9774d56d682e549c".equals(androidId)) {
            byte[] bytes = new byte[8];
            new SecureRandom().nextBytes(bytes);
            char[] alphabet = "0123456789abcdef".toCharArray();
            char[] out = new char[16];
            for (int i = 0; i < 8; i++) {
                int value = bytes[i] & 0xff;
                out[i * 2] = alphabet[value >>> 4];
                out[i * 2 + 1] = alphabet[value & 0x0f];
            }
            androidId = new String(out);
        }
        prefs.edit().putString("device", androidId).apply();
        return androidId;
    }

    private void hideKeyboard() {
        InputMethodManager imm = (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
        if (imm != null && getCurrentFocus() != null) {
            imm.hideSoftInputFromWindow(getCurrentFocus().getWindowToken(), 0);
        }
    }
}
