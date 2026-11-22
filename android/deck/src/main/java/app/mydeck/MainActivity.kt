package app.mydeck

import android.app.Activity
import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.os.Build
import android.os.Bundle
import android.os.SystemClock
import android.provider.Settings
import android.util.DisplayMetrics
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.FrameLayout
import org.json.JSONObject

class MainActivity : Activity(), Connection.Listener, DeckView.Listener {
    private lateinit var deck: DeckView
    private lateinit var overlay: StatusOverlay
    private lateinit var connection: Connection
    private lateinit var updater: UpdateBar
    private lateinit var wall: WallPlayer
    private lateinit var screen: ScreenManager
    private val prefs by lazy { getSharedPreferences("mydeck", MODE_PRIVATE) }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        @Suppress("DEPRECATION")
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON or WindowManager.LayoutParams.FLAG_SHOW_WHEN_LOCKED or
            WindowManager.LayoutParams.FLAG_DISMISS_KEYGUARD or WindowManager.LayoutParams.FLAG_TURN_SCREEN_ON)
        screen = ScreenManager(this)

        deck = DeckView(this).apply { listener = this@MainActivity }
        overlay = StatusOverlay(this)
        val root = FrameLayout(this).apply { setBackgroundColor(Color.parseColor("#07090E")) }
        AssetCache.init(this)
        wall = WallPlayer(this, deck)
        deck.onWallChanged = { w -> wall.show(w) }

        root.addView(wall.surface, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        root.addView(deck, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        root.addView(overlay, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        updater = UpdateBar(this)
        root.addView(updater, FrameLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT,
            android.view.Gravity.BOTTOM or android.view.Gravity.CENTER_HORIZONTAL))
        Thread { UpdateBar.installedHash(this) }.start()
        setContentView(root)

        loadSavedLayout()
        connection = Connection(this, ::helloMessage, this)
        overlay.isLongClickable = true
        overlay.setOnLongClickListener { askAddress(); true }

        connection.start()
        screen.start()
        applyBrightness(prefs.getFloat("brightness", 0.8f))
        TimerStore.onDone = { id, label -> onTimerDone(id, label) }
    }

    private fun onTimerDone(id: String, label: String) {
        screen.alert()
        deck.showToast("$label bitti")
        connection.send(JSONObject().put("t", "timerdone").put("tile", id))
        try {
            val tone = android.media.ToneGenerator(android.media.AudioManager.STREAM_ALARM, 90)
            val h = android.os.Handler(mainLooper)
            for (i in 0 until 3) h.postDelayed({ tone.startTone(android.media.ToneGenerator.TONE_PROP_BEEP2, 350) }, i * 600L)
            h.postDelayed({ tone.release() }, 2400)
        } catch (_: RuntimeException) { }
    }

    override fun onDestroy() {
        connection.stop()
        screen.stop()
        super.onDestroy()
    }

    private fun applyBrightness(v: Float) {
        val b = v.coerceIn(0.02f, 1f)
        window.attributes = window.attributes.apply { screenBrightness = b }
        deck.brightness = b
    }

    override fun onBrightness(value: Float) {
        applyBrightness(value)
        prefs.edit().putFloat("brightness", value.coerceIn(0.02f, 1f)).apply()
    }

    override fun onAnyTouch() = screen.touched()

    override fun onScreen(pcIdle: Boolean) = screen.onPcIdle(pcIdle)

    override fun onResume() {
        super.onResume()
        immersive()
        screen.resumed()
        wall.resume()
    }

    override fun onPause() {
        super.onPause()
        wall.pause()
    }

    override fun onWindowFocusChanged(hasFocus: Boolean) {
        super.onWindowFocusChanged(hasFocus)
        if (hasFocus) immersive()
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        if (!deck.onBack()) super.onBackPressed()
    }

    private fun immersive() {
        @Suppress("DEPRECATION")
        window.decorView.systemUiVisibility = (View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
            or View.SYSTEM_UI_FLAG_FULLSCREEN
            or View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
            or View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
            or View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
            or View.SYSTEM_UI_FLAG_LAYOUT_STABLE)
    }

    private fun helloMessage(): JSONObject {
        val w = if (deck.width > 0) deck.width else realMetrics().widthPixels
        val h = if (deck.height > 0) deck.height else realMetrics().heightPixels
        return JSONObject()
            .put("t", "hello")
            .put("id", Settings.Secure.getString(contentResolver, Settings.Secure.ANDROID_ID) ?: "unknown")
            .put("name", "${Build.MANUFACTURER} ${Build.MODEL}".trim())
            .put("w", w)
            .put("h", h)
            .put("density", resources.displayMetrics.density.toDouble())
            .put("app", 2)
            .put("os", Build.VERSION.RELEASE ?: "")
            .put("sdk", Build.VERSION.SDK_INT)
            .put("lang", java.util.Locale.getDefault().language)
            .put("apk", UpdateBar.installedHash(this))
            .put("key", prefs.getString("key", "") ?: "")
    }

    private val layoutFile by lazy { java.io.File(filesDir, "son-duzen.json") }
    private var lastState = Connection.State.SEARCHING
    private var lastDetail = ""

    private fun loadSavedLayout() {
        Thread({
            try {
                if (!layoutFile.exists()) return@Thread
                val l = Layout.parse(JSONObject(layoutFile.readText()))
                runOnUiThread {
                    if (deck.hasLayout) return@runOnUiThread
                    screen.idleSec = l.idleSec
                    screen.sleepWhenPcOff = l.sleepWhenPcOff
                    deck.setLayout(l)
                    overlay.set(lastState, lastDetail, true)
                }
            } catch (_: Exception) {
                layoutFile.delete()
            }
        }, "mydeck-son-duzen").start()
    }

    override fun onLayoutJson(json: String) {
        try {
            val tmp = java.io.File(filesDir, "son-duzen.json.part")
            tmp.writeText(json)
            if (!tmp.renameTo(layoutFile)) { layoutFile.delete(); tmp.renameTo(layoutFile) }
        } catch (_: Exception) { }
    }

    override fun onKey(key: String) {
        prefs.edit().putString("key", key).commit()
    }

    private fun realMetrics(): DisplayMetrics {
        val dm = DisplayMetrics()
        @Suppress("DEPRECATION")
        windowManager.defaultDisplay.getRealMetrics(dm)
        return dm
    }

    override fun onStatus(state: Connection.State, detail: String) {
        lastState = state; lastDetail = detail
        screen.onConnected(state == Connection.State.CONNECTED)
        overlay.set(state, detail, deck.hasLayout)
    }

    private fun askAddress() {
        if (lastState == Connection.State.CONNECTED) return
        val input = android.widget.EditText(this).apply {
            setText(connection.manualHost() ?: "")
            hint = "192.168.1.20"
            inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_URI
            setSingleLine()
        }
        val pad = (20 * resources.displayMetrics.density).toInt()
        val box = FrameLayout(this).apply { setPadding(pad, pad / 2, pad, 0); addView(input) }
        android.app.AlertDialog.Builder(this)
            .setTitle(I18n.t("Computer address"))
            .setMessage(I18n.t("The IP address is shown in the MyDeck window on your PC. Leave it empty to search automatically."))
            .setView(box)
            .setPositiveButton(I18n.t("Connect")) { _, _ -> connection.setManualHost(input.text.toString()) }
            .setNeutralButton(I18n.t("Search automatically")) { _, _ -> connection.setManualHost(null) }
            .setNegativeButton(I18n.t("Cancel"), null)
            .show()
    }

    override fun onPairCode(code: String) {
        overlay.pairCode = code
        overlay.invalidate()
    }

    override fun onLayout(layout: Layout) {
        screen.idleSec = layout.idleSec
        screen.sleepWhenPcOff = layout.sleepWhenPcOff
        screen.ensureAdmin()
        deck.setLayout(layout)
        overlay.set(Connection.State.CONNECTED, "", true)
    }

    override fun onToast(text: String) = deck.showToast(text)

    override fun onState(states: JSONObject) = deck.applyStates(states)

    override fun onUpdate(url: String) = updater.offer(url)

    override fun onSlideMute(tile: DeckTile) {
        connection.send(JSONObject().put("t", "slidemute").put("tile", tile.id))
    }

    override fun onMedia(tile: DeckTile, cmd: String, value: Float) {
        connection.send(JSONObject().put("t", "media").put("tile", tile.id).put("cmd", cmd).put("v", value.toDouble()))
    }

    override fun onSlide(tile: DeckTile, value: Float) {
        connection.send(JSONObject().put("t", "slide").put("tile", tile.id).put("v", value.toDouble()))
    }

    override fun onPress(tile: DeckTile, gesture: String) {
        val m = JSONObject().put("t", "press").put("tile", tile.id)
        if (gesture != "tap") m.put("g", gesture)
        connection.send(m)
    }

    override fun onPageShown(pageId: String) {
        connection.send(JSONObject().put("t", "page").put("id", pageId))
    }

    override fun onGoto(pageId: String?, back: Boolean) {
        if (back) deck.goBack() else if (pageId != null) deck.showPage(pageId)
    }

    class StatusOverlay(context: Context) : View(context) {
        private var state = Connection.State.SEARCHING
        private var detail = ""
        private var dimmed = false
        var pairCode: String? = null
        private val d = resources.displayMetrics.density
        private val p = Paint(Paint.ANTI_ALIAS_FLAG)
        private val r = RectF()
        private val startAt = SystemClock.uptimeMillis()

        fun set(s: Connection.State, text: String, hasLayout: Boolean) {
            state = s
            detail = text
            dimmed = hasLayout
            if (s != Connection.State.PAIRING) pairCode = null
            visibility = if (s == Connection.State.CONNECTED) GONE else VISIBLE
            invalidate()
        }

        override fun onDraw(c: Canvas) {
            if (state == Connection.State.CONNECTED) return
            c.drawColor(if (dimmed) Color.argb(200, 7, 9, 14) else Color.rgb(7, 9, 14))

            val cw = 420 * d
            val ch = if (state == Connection.State.PAIRING) 250 * d else 170 * d
            r.set(width / 2f - cw / 2, height / 2f - ch / 2, width / 2f + cw / 2, height / 2f + ch / 2)
            p.style = Paint.Style.FILL
            p.color = Color.parseColor("#11151F")
            c.drawRoundRect(r, 20 * d, 20 * d, p)
            p.style = Paint.Style.STROKE
            p.strokeWidth = d
            p.color = Color.parseColor("#222938")
            c.drawRoundRect(r, 20 * d, 20 * d, p)
            p.style = Paint.Style.FILL

            val s = 14 * d; val g = 4 * d
            val lx = r.centerX() - s - g / 2; val ly = r.top + 28 * d
            val colors = intArrayOf(0xFFFF3D5A.toInt(), 0xFF3CC8F0.toInt(), 0xFF3CC8F0.toInt(), 0xFF5A6680.toInt())
            val t = (SystemClock.uptimeMillis() - startAt) / 1000f
            for (i in 0..3) {
                val busy = state == Connection.State.SEARCHING || state == Connection.State.CONNECTING
                p.color = colors[i]
                p.alpha = if (busy) (150 + 105 * Math.sin((t * 4 - i).toDouble())).toInt().coerceIn(60, 255) else 255
                val x = lx + (i % 2) * (s + g); val y = ly + (i / 2) * (s + g)
                c.drawRoundRect(RectF(x, y, x + s, y + s), 4 * d, 4 * d, p)
            }
            p.alpha = 255

            val title = when (state) {
                Connection.State.SEARCHING -> I18n.t("Looking for your computer")
                Connection.State.CONNECTING -> I18n.t("Connecting")
                Connection.State.PAIRING -> I18n.t("Pairing code")
                Connection.State.DENIED -> I18n.t("Pairing declined")
                Connection.State.CONNECTED -> ""
            }
            text(c, title, 20f, Color.parseColor("#E8ECF4"), r.top + 104 * d, bold = true)
            if (state == Connection.State.PAIRING) {
                text(c, pairCode ?: "····", 54f, Color.parseColor("#FF3D5A"), r.top + 172 * d, bold = true, spacing = 0.25f)
                text(c, I18n.t("Confirm the same code in the MyDeck window on your computer"), 13f, Color.parseColor("#8D97AB"), r.top + 214 * d)
            } else {
                val sub = when (state) {
                    Connection.State.SEARCHING -> I18n.t("Make sure MyDeck is open on your computer and on the same Wi-Fi. Long-press to enter its address.")
                    Connection.State.DENIED -> "10 saniye sonra yeniden denenecek"
                    else -> detail
                }
                text(c, sub, 13f, Color.parseColor("#8D97AB"), r.top + 134 * d)
            }
            if (state == Connection.State.SEARCHING || state == Connection.State.CONNECTING) postInvalidateDelayed(50)
        }

        private fun text(c: Canvas, s: String, sp: Float, color: Int, baseline: Float, bold: Boolean = false, spacing: Float = 0f) {
            p.color = color
            p.textSize = sp * d
            p.typeface = if (bold) Typeface.create("sans-serif-medium", Typeface.NORMAL) else Typeface.DEFAULT
            p.letterSpacing = spacing
            c.drawText(s, width / 2f - p.measureText(s) / 2f, baseline, p)
            p.letterSpacing = 0f
        }
    }
}
