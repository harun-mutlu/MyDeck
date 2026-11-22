package app.mydeck

import android.content.Context
import android.content.Intent
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.graphics.Typeface
import android.net.Uri
import android.os.SystemClock
import android.view.MotionEvent
import android.view.View
import java.io.File
import java.io.FileInputStream
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

class UpdateBar(context: Context) : View(context) {
    private enum class Mode { HIDDEN, READY, DOWNLOADING, FAILED }

    private var mode = Mode.HIDDEN
    private var url = ""
    private var progress = 0f
    private var shownAt = 0L
    private val d = resources.displayMetrics.density

    private val bg = Paint(Paint.ANTI_ALIAS_FLAG)
    private val bar = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(70, 255, 255, 255) }
    private val text = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        color = Color.WHITE
        textSize = 15f * d
        typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    }
    private val rect = RectF()

    init {
        visibility = GONE
        setLayerType(LAYER_TYPE_SOFTWARE, null)
    }

    fun offer(newUrl: String) {
        if (mode == Mode.DOWNLOADING) return
        url = newUrl
        setMode(Mode.READY)
    }

    private fun setMode(m: Mode) {
        mode = m
        if (m == Mode.HIDDEN) { visibility = GONE; return }
        if (visibility != VISIBLE) { visibility = VISIBLE; shownAt = SystemClock.uptimeMillis() }
        requestLayout()
        invalidate()
    }

    private fun label(): String = when (mode) {
        Mode.READY -> I18n.t("New version ready  ·  Update")
        Mode.DOWNLOADING -> I18n.t("Downloading  {0}%", (progress * 100).toInt())
        Mode.FAILED -> I18n.t("Download failed  ·  Try again")
        Mode.HIDDEN -> ""
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val w = text.measureText(label()) + 48f * d
        setMeasuredDimension(w.toInt(), (44f * d + 16f * d).toInt())
    }

    override fun onDraw(c: Canvas) {
        if (mode == Mode.HIDDEN) return

        val t = ((SystemClock.uptimeMillis() - shownAt) / 220f).coerceIn(0f, 1f)
        val ease = 1f - (1f - t) * (1f - t) * (1f - t)
        val dy = (1f - ease) * 30f * d
        rect.set(0f, dy, width.toFloat(), height - 16f * d + dy)
        val r = rect.height() / 2f
        bg.color = if (mode == Mode.FAILED) Color.parseColor("#5A6680") else Color.parseColor("#FF3D5A")
        bg.setShadowLayer(10f * d, 0f, 3f * d, Color.argb(140, 0, 0, 0))
        c.drawRoundRect(rect, r, r, bg)
        if (mode == Mode.DOWNLOADING) {
            c.save()
            c.clipRect(rect.left, rect.top, rect.left + rect.width() * progress, rect.bottom)
            c.drawRoundRect(rect, r, r, bar)
            c.restore()
        }
        val s = label()
        val fm = text.fontMetrics
        c.drawText(s, (width - text.measureText(s)) / 2f, rect.centerY() - (fm.ascent + fm.descent) / 2f, text)
        if (t < 1f) postInvalidateOnAnimation()
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (e.action == MotionEvent.ACTION_UP && (mode == Mode.READY || mode == Mode.FAILED)) download()
        return true
    }

    private fun download() {
        progress = 0f
        setMode(Mode.DOWNLOADING)
        val target = url
        Thread {
            try {
                val file = apkFile(context)
                val conn = URL(target).openConnection() as HttpURLConnection
                conn.connectTimeout = 5000
                conn.readTimeout = 15000
                val total = conn.contentLength.toLong()
                var done = 0L
                var lastUi = 0L
                conn.inputStream.use { input ->
                    file.outputStream().use { out ->
                        val buf = ByteArray(64 * 1024)
                        while (true) {
                            val n = input.read(buf)
                            if (n < 0) break
                            out.write(buf, 0, n)
                            done += n
                            val now = SystemClock.uptimeMillis()
                            if (total > 0 && now - lastUi > 80) {
                                lastUi = now
                                val p = done.toFloat() / total
                                post { progress = p; requestLayout(); invalidate() }
                            }
                        }
                    }
                }
                conn.disconnect()
                if (total > 0 && done != total) throw IllegalStateException("eksik indirme")
                file.setReadable(true, false)
                post {
                    setMode(Mode.HIDDEN)
                    val i = Intent(Intent.ACTION_VIEW)
                        .setDataAndType(Uri.fromFile(file), "application/vnd.android.package-archive")
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
                    context.startActivity(i)
                }
            } catch (_: Exception) {
                post { setMode(Mode.FAILED) }
            }
        }.start()
    }

    companion object {
        @Volatile private var hash: String? = null

        private fun apkFile(ctx: Context): File {
            val dir = ctx.externalCacheDir ?: ctx.cacheDir
            dir.mkdirs()
            return File(dir, "MyDeck-update.apk")
        }

        fun installedHash(ctx: Context): String {
            hash?.let { return it }
            synchronized(this) {
                hash?.let { return it }
                val h = try {
                    val md = MessageDigest.getInstance("SHA-1")
                    FileInputStream(ctx.applicationInfo.sourceDir).use { input ->
                        val buf = ByteArray(64 * 1024)
                        while (true) { val n = input.read(buf); if (n < 0) break; md.update(buf, 0, n) }
                    }
                    md.digest().joinToString("") { "%02x".format(it) }
                } catch (_: Exception) { "" }
                hash = h
                return h
            }
        }
    }
}
