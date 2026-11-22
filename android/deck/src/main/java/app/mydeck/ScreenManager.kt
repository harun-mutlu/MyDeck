package app.mydeck

import android.app.Activity
import android.app.admin.DeviceAdminReceiver
import android.app.admin.DevicePolicyManager
import android.content.BroadcastReceiver
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.net.wifi.WifiManager
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import android.os.SystemClock

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_BOOT_COMPLETED) return
        context.startActivity(Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }
}

class AdminReceiver : DeviceAdminReceiver()

class ScreenManager(private val activity: Activity) {
    private val pm = activity.getSystemService(Context.POWER_SERVICE) as PowerManager
    private val dpm = activity.getSystemService(Context.DEVICE_POLICY_SERVICE) as DevicePolicyManager
    private val admin = ComponentName(activity, AdminReceiver::class.java)
    private val cpuLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "mydeck:baglanti")
    private val wifiLock = (activity.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager)
        .createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "mydeck:wifi")
    private val main = Handler(Looper.getMainLooper())

    var idleSec = 600
    var sleepWhenPcOff = true

    private var pcIdle = false
    private var connected = false
    private var lostSince = 0L
    private var lastTouch = SystemClock.uptimeMillis()
    private var askedAdmin = false

    private val ticker = object : Runnable {
        override fun run() { check(); main.postDelayed(this, 5000) }
    }

    fun start() {
        try { if (!cpuLock.isHeld) cpuLock.acquire() } catch (_: Exception) {}
        try { if (!wifiLock.isHeld) wifiLock.acquire() } catch (_: Exception) {}
        main.removeCallbacks(ticker)
        main.postDelayed(ticker, 5000)
    }

    fun stop() {
        main.removeCallbacks(ticker)
        try { if (cpuLock.isHeld) cpuLock.release() } catch (_: Exception) {}
        try { if (wifiLock.isHeld) wifiLock.release() } catch (_: Exception) {}
    }

    fun touched() { lastTouch = SystemClock.uptimeMillis() }

    fun resumed() { lastTouch = SystemClock.uptimeMillis() }

    fun onPcIdle(idle: Boolean) {
        val was = pcIdle
        pcIdle = idle
        if (was && !idle) wake()
        check()
    }

    fun onConnected(ok: Boolean) {
        if (ok && !connected) { lostSince = 0L; if (!pcIdle) wake() }
        if (!ok && connected) lostSince = SystemClock.uptimeMillis()
        if (!ok && lostSince == 0L) lostSince = SystemClock.uptimeMillis()
        connected = ok
    }

    fun ensureAdmin() {
        if (askedAdmin || dpm.isAdminActive(admin) || (idleSec <= 0 && !sleepWhenPcOff)) return
        askedAdmin = true
        try {
            activity.startActivity(Intent(DevicePolicyManager.ACTION_ADD_DEVICE_ADMIN)
                .putExtra(DevicePolicyManager.EXTRA_DEVICE_ADMIN, admin)
                .putExtra(DevicePolicyManager.EXTRA_ADD_EXPLANATION,
                    I18n.t("MyDeck only uses the \"lock screen\" permission, to turn the tablet screen off while your computer is idle or off.")))
        } catch (_: Exception) {}
    }

    private fun check() {
        val now = SystemClock.uptimeMillis()
        @Suppress("DEPRECATION")
        val on = pm.isScreenOn
        if (!on) return
        val idleMs = idleSec * 1000L
        val sleepIdle = idleSec > 0 && pcIdle && now - lastTouch > idleMs
        val sleepOff = sleepWhenPcOff && !connected && lostSince > 0 && now - lostSince > 60_000 && now - lastTouch > 60_000
        if (sleepIdle || sleepOff) sleep()
    }

    private fun sleep() {
        if (!dpm.isAdminActive(admin)) return
        try { dpm.lockNow() } catch (_: Exception) {}
    }

    fun alert() { lastTouch = SystemClock.uptimeMillis(); wake() }

    private fun wake() {
        @Suppress("DEPRECATION")
        if (pm.isScreenOn) return
        try {
            @Suppress("DEPRECATION")
            val wl = pm.newWakeLock(PowerManager.SCREEN_BRIGHT_WAKE_LOCK or PowerManager.ACQUIRE_CAUSES_WAKEUP or PowerManager.ON_AFTER_RELEASE, "mydeck:uyan")
            wl.acquire(3000)
        } catch (_: Exception) {}
        lastTouch = SystemClock.uptimeMillis()
        activity.startActivity(Intent(activity, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_REORDER_TO_FRONT))
    }
}
