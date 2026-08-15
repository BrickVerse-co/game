package gg.brickverse.webview;

import android.app.Dialog;
import android.graphics.Color;
import android.net.Uri;
import android.os.Bundle;
import android.view.ViewGroup;
import android.view.Window;
import android.webkit.CookieManager;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import org.godotengine.godot.Godot;
import org.godotengine.godot.plugin.GodotPlugin;
import org.godotengine.godot.plugin.SignalInfo;
import org.godotengine.godot.plugin.UsedByGodot;

import java.util.Collections;
import java.util.Set;

/** Full-screen Android WebView used only for BrickVerse authentication. */
public final class BrickVerseWebViewPlugin extends GodotPlugin {
    private static final SignalInfo URL_RECEIVED = new SignalInfo("url_received", String.class);
    private Dialog dialog;
    private WebView webView;

    public BrickVerseWebViewPlugin(Godot godot) { super(godot); }

    @Override public String getPluginName() { return "BrickVerseWebView"; }
    @Override public Set<SignalInfo> getPluginSignals() { return Collections.singleton(URL_RECEIVED); }

    @UsedByGodot
    public void open_auth_url(String rawUrl) {
        runOnUiThread(() -> showWebView(rawUrl));
    }

    @UsedByGodot
    public void close() {
        runOnUiThread(this::dismiss);
    }

    @Override public void onMainDestroy() {
        dismiss();
        super.onMainDestroy();
    }

    private void showWebView(String rawUrl) {
        if (getActivity() == null || rawUrl == null || rawUrl.isEmpty()) return;
        dismiss();

        dialog = new Dialog(getActivity(), android.R.style.Theme_DeviceDefault_NoActionBar);
        dialog.requestWindowFeature(Window.FEATURE_NO_TITLE);
        webView = new WebView(getActivity());
        webView.setBackgroundColor(Color.rgb(13, 28, 45));

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setSupportZoom(false);
        settings.setBuiltInZoomControls(false);
        settings.setUserAgentString(settings.getUserAgentString() + " BrickVerseMobile/1.0");
        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(webView, true);
        webView.setWebChromeClient(new WebChromeClient());
        webView.setWebViewClient(new WebViewClient() {
            private boolean route(Uri uri) {
                if (uri != null && "brickverse".equalsIgnoreCase(uri.getScheme())) {
                    emitSignal(URL_RECEIVED, uri.toString());
                    dismiss();
                    return true;
                }
                return false;
            }
            @Override public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                return route(request.getUrl());
            }
            @Override public boolean shouldOverrideUrlLoading(WebView view, String url) {
                return route(Uri.parse(url));
            }
        });

        dialog.setContentView(webView, new ViewGroup.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        dialog.setOnDismissListener(ignored -> destroyWebView());
        dialog.setOnKeyListener((ignored, keyCode, event) -> {
            if (keyCode == android.view.KeyEvent.KEYCODE_BACK && event.getAction() == android.view.KeyEvent.ACTION_UP) {
                if (webView != null && webView.canGoBack()) webView.goBack(); else dismiss();
                return true;
            }
            return false;
        });
        dialog.show();
        if (dialog.getWindow() != null) dialog.getWindow().setLayout(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
        webView.loadUrl(rawUrl);
    }

    private void dismiss() {
        Dialog current = dialog;
        dialog = null;
        if (current != null && current.isShowing()) current.dismiss(); else destroyWebView();
    }

    private void destroyWebView() {
        WebView current = webView;
        webView = null;
        if (current != null) {
            current.stopLoading();
            current.loadUrl("about:blank");
            current.clearHistory();
            current.removeAllViews();
            current.destroy();
        }
    }
}
