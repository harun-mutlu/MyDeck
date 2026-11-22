package app.mydeck

import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.os.SystemClock
import java.text.SimpleDateFormat
import java.util.Calendar
import java.util.Locale
import java.util.TimeZone
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

class WidgetRenderer(private val d: Float, context: android.content.Context) {
    private val weatherArtwork by lazy { WeatherArtwork(context.applicationContext) }

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val text = Paint(Paint.ANTI_ALIAS_FLAG)
    private val light = Typeface.create("sans-serif-light", Typeface.NORMAL)
    private val regular = Typeface.create("sans-serif", Typeface.NORMAL)
    private val medium = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    private val bold = Typeface.create("sans-serif", Typeface.BOLD)
    private val rect = RectF()
    private val path = Path()
    private val TR: Locale get() = if (I18n.lang == "tr") Locale("tr", "TR") else Locale.US
    private val zones = HashMap<String, TimeZone>()

    private var ink = 0
    private var sub = 0

    private fun frame(c: Canvas, r: RectF, th: Theme, accent: Int?, k: Float, alpha: Int) {
        fill.shader = null
        fill.color = if (accent != null) mix(th.tile, accent, k) else th.tile
        fill.alpha = alpha
        c.drawRoundRect(r, th.radius, th.radius, fill)
        fill.alpha = 255
        stroke.strokeWidth = max(1f, d)
        stroke.color = if (accent != null) mix(th.border, accent, 0.75f) else th.border
        val h = stroke.strokeWidth / 2
        rect.set(r.left + h, r.top + h, r.right - h, r.bottom - h)
        c.drawRoundRect(rect, th.radius - h, th.radius - h, stroke)
    }

    private fun caption(c: Canvas, s: String, x: Float, y: Float, align: Paint.Align = Paint.Align.LEFT, size: Float = 11 * d, maxW: Float = 0f) {
        text.typeface = bold; text.textSize = size; text.color = sub; text.textAlign = align
        text.letterSpacing = 0.12f
        var str = s.uppercase(TR)
        if (maxW > 0) while (str.length > 1 && text.measureText(str) > maxW) str = str.dropLast(2) + "…"
        c.drawText(str, x, y, text)
        text.letterSpacing = 0f
    }

    private fun put(c: Canvas, s: String, x: Float, y: Float, size: Float, color: Int, face: Typeface, align: Paint.Align = Paint.Align.LEFT, maxW: Float = 0f) {
        text.typeface = face; text.textSize = size; text.color = color; text.textAlign = align
        var str = s
        if (maxW > 0) while (str.length > 1 && text.measureText(str) > maxW) str = str.dropLast(2) + "…"
        c.drawText(str, x, y, text)
    }

    private fun zone(id: String?): TimeZone =
        if (id.isNullOrEmpty()) TimeZone.getDefault() else zones.getOrPut(id) { TimeZone.getTimeZone(id) }

    private fun two(n: Int) = if (n < 10) "0$n" else n.toString()

    fun drawClock(c: Canvas, t: DeckTile, r: RectF, th: Theme, alpha: Int): Long {
        val spec = t.clock ?: return 0
        ink = th.text; sub = th.sub
        frame(c, r, th, t.color, 0.12f, alpha)
        val tz = zone(spec.tz)
        val cal = Calendar.getInstance(tz)
        val ms = System.currentTimeMillis() % 1000
        if (spec.style == "analog") analog(c, t, r, th, spec, cal, ms) else digital(c, t, r, th, spec, cal, ms)
        return if (spec.sec || spec.style == "analog") 1000 - ms + 5 else (60 - cal.get(Calendar.SECOND)) * 1000L - ms + 5
    }

    private fun digital(c: Canvas, t: DeckTile, r: RectF, th: Theme, spec: ClockSpec, cal: Calendar, ms: Long) {
        val w = r.width(); val h = r.height()
        val p = if (w < 200 * d) 12 * d else 18 * d
        val accent = t.color ?: th.accent
        val city = spec.city
        if (!city.isNullOrEmpty()) caption(c, city, r.left + p, r.top + p + 10 * d)
        val hm = "${two(cal.get(Calendar.HOUR_OF_DAY))}:${two(cal.get(Calendar.MINUTE))}"
        val ss = two(cal.get(Calendar.SECOND))
        val top = r.top + p + (if (city.isNullOrEmpty()) 0f else 14 * d)
        val dateH = if (spec.date) max(14 * d, min(18 * d, h * 0.1f)) + 10 * d else 0f
        val barH = if (spec.sec) 8 * d else 0f
        val bottom = r.bottom - p - dateH - barH
        var size = min((bottom - top) * 0.92f, h * 0.5f)
        text.typeface = light
        fun width(s: Float): Float {
            text.textSize = s; val a = text.measureText(hm)
            if (!spec.sec) return a
            text.textSize = s * 0.3f; return a + 6 * d + text.measureText(ss)
        }
        while (size > 12 * d && width(size) > w - 2 * p) size *= 0.94f
        val base = (top + bottom) / 2 + size * 0.36f
        put(c, hm, r.left + p - size * 0.03f, base, size, ink, light)
        if (spec.sec) {
            text.textSize = size; val hw = text.measureText(hm)
            put(c, ss, r.left + p + hw + 6 * d, base, size * 0.3f, sub, regular)
        }
        if (spec.date) {
            val ds = max(12 * d, min(16 * d, h * 0.09f))
            val fmt = SimpleDateFormat(if (w < 220 * d) "d MMM EEE" else "d MMMM EEEE", TR).apply { timeZone = cal.timeZone }
            put(c, fmt.format(cal.time), r.left + p, r.bottom - p - barH, ds, ink, medium, maxW = w - 2 * p)
        }
        if (spec.sec) {
            val y = r.bottom - p * 0.75f
            val f = (cal.get(Calendar.SECOND) + ms / 1000f) / 60f
            fill.color = withAlpha(sub, 0.15f); c.drawRect(r.left + p, y, r.right - p, y + 2 * d, fill)
            fill.color = accent; c.drawRect(r.left + p, y, r.left + p + (w - 2 * p) * f, y + 2 * d, fill)
        }
    }

    private fun analog(c: Canvas, t: DeckTile, r: RectF, th: Theme, spec: ClockSpec, cal: Calendar, ms: Long) {
        val accent = t.color ?: th.accent
        val labelH = 26 * d
        val cx = r.centerX(); val cy = r.top + (r.height() - labelH) / 2 + 4 * d
        val rad = min(r.width(), r.height() - labelH) * 0.4f
        stroke.color = withAlpha(sub, 0.25f); stroke.strokeWidth = d
        c.drawCircle(cx, cy, rad, stroke)
        for (i in 0 until 12) {
            val a = i * PI / 6
            val major = i % 3 == 0
            val l = (if (major) 0.12f else 0.06f) * rad
            stroke.color = if (major) ink else withAlpha(sub, 0.6f)
            stroke.strokeWidth = (if (major) 2f else 1.2f) * d
            val r0 = rad - 3 * d
            c.drawLine(cx + (sin(a) * r0).toFloat(), cy - (cos(a) * r0).toFloat(),
                cx + (sin(a) * (r0 - l)).toFloat(), cy - (cos(a) * (r0 - l)).toFloat(), stroke)
        }
        val s = cal.get(Calendar.SECOND).toFloat()
        val m = cal.get(Calendar.MINUTE) + s / 60f
        val hr = cal.get(Calendar.HOUR) + m / 60f
        stroke.strokeCap = Paint.Cap.ROUND
        fun hand(ang: Double, len: Float, w: Float, col: Int) {
            stroke.color = col; stroke.strokeWidth = w
            c.drawLine(cx - (sin(ang) * 6 * d).toFloat(), cy + (cos(ang) * 6 * d).toFloat(),
                cx + (sin(ang) * len).toFloat(), cy - (cos(ang) * len).toFloat(), stroke)
        }
        hand(hr * PI / 6, rad * 0.5f, max(3f, rad * 0.06f), ink)
        hand(m * PI / 30, rad * 0.76f, max(2.4f, rad * 0.045f), ink)
        if (spec.sec) hand(s * PI / 30, rad * 0.86f, 1.4f * d, accent)
        stroke.strokeCap = Paint.Cap.BUTT
        fill.color = accent; c.drawCircle(cx, cy, 3.5f * d, fill)
        val name = spec.city ?: ""
        val line = if (!spec.tz.isNullOrEmpty()) "$name  ${two(cal.get(Calendar.HOUR_OF_DAY))}:${two(cal.get(Calendar.MINUTE))}".trim()
            else if (spec.date) SimpleDateFormat("d MMM EEE", TR).format(cal.time) else name
        if (line.isNotEmpty()) put(c, line, cx, r.bottom - 14 * d, 13 * d, ink, medium, Paint.Align.CENTER, r.width() - 16 * d)
    }

    fun timerHit(t: DeckTile, r: RectF, x: Float, y: Float): String {
        val stopwatch = t.timer?.mode == "stopwatch"
        if (y > r.bottom - 44 * d && x < r.left + 52 * d) return "reset"
        if (!stopwatch && y < r.top + 44 * d && x > r.right - 56 * d) return "plus"
        return "toggle"
    }

    fun drawTimer(c: Canvas, t: DeckTile, r: RectF, th: Theme, alpha: Int): Long {
        val spec = t.timer ?: return 0
        ink = th.text; sub = th.sub
        val s = TimerStore.get(t.id)
        val stopwatch = spec.mode == "stopwatch"
        val color = t.color ?: if (stopwatch) Color.parseColor("#3CC8F0") else th.accent
        val now = SystemClock.uptimeMillis()
        val flashing = s != null && s.doneAt > 0 && now - s.doneAt < 6000
        val blink = flashing && ((now - s!!.doneAt) / 300) % 2 == 0L
        val running = s?.running == true
        frame(c, r, th, color, if (blink) 0.45f else if (running) 0.2f else 0.1f, alpha)
        val big = r.width() > 220 * d && r.height() > 220 * d
        val p = 14 * d
        caption(c, t.label ?: if (stopwatch) "Kronometre" else I18n.t("Timer"), r.left + p, r.top + p + 10 * d)
        if (!stopwatch) put(c, "+1", r.right - p, r.top + p + 10 * d, 13 * d, sub, medium, Paint.Align.RIGHT)

        val v = s?.value() ?: (spec.secs * 1000L)
        val total = s?.total ?: (spec.secs * 1000L)
        val cx = r.centerX(); val cy = r.centerY() + 6 * d
        val rad = min(r.width(), r.height() - 40 * d) * (if (big) 0.36f else 0.33f)
        val ring = max(5f * d, rad * 0.11f)
        stroke.strokeWidth = ring; stroke.color = withAlpha(sub, 0.14f)
        c.drawCircle(cx, cy, rad, stroke)
        val f = if (stopwatch) ((v % 60_000L) / 60_000f) else if (total > 0) v.toFloat() / total else 0f
        stroke.color = color; stroke.strokeCap = Paint.Cap.ROUND
        rect.set(cx - rad, cy - rad, cx + rad, cy + rad)
        c.drawArc(rect, -90f, 360f * f.coerceIn(0.002f, 1f), false, stroke)
        stroke.strokeCap = Paint.Cap.BUTT

        val secs = if (stopwatch) v / 1000 else (v + 999) / 1000
        val hh = secs / 3600; val mm = (secs % 3600) / 60; val ss = secs % 60
        val txt = if (hh > 0) "$hh:${two(mm.toInt())}:${two(ss.toInt())}" else "${two(mm.toInt())}:${two(ss.toInt())}"
        var ts = rad * (if (hh > 0) 0.42f else 0.52f)
        put(c, txt, cx, cy + ts * 0.3f, ts, ink, medium, Paint.Align.CENTER)
        val state = when {
            flashing -> "Bitti!"
            running -> I18n.t("running")
            s != null && s.touched -> I18n.t("paused")
            else -> I18n.t("start")
        }
        ts = max(10 * d, rad * 0.2f)
        put(c, state, cx, cy + rad * 0.48f, ts, if (flashing) ink else sub, regular, Paint.Align.CENTER)
        resetIcon(c, r.left + p + 6 * d, r.bottom - p - 5 * d, 7 * d, sub)
        return when { flashing -> 150; running -> if (stopwatch) 100 else 200; else -> 0 }
    }

    private fun resetIcon(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        stroke.color = color; stroke.strokeWidth = 1.8f * d; stroke.strokeCap = Paint.Cap.ROUND
        rect.set(cx - s, cy - s, cx + s, cy + s)
        c.drawArc(rect, -60f, 300f, false, stroke)
        stroke.strokeCap = Paint.Cap.BUTT
        val a = -60.0 * PI / 180
        val ex = cx + (cos(a) * s).toFloat(); val ey = cy + (sin(a) * s).toFloat()
        path.reset(); path.moveTo(ex - 4 * d, ey - 3 * d); path.lineTo(ex + 1.5f * d, ey + 0.5f * d); path.lineTo(ex - 2 * d, ey + 4 * d); path.close()
        fill.color = color; c.drawPath(path, fill)
    }

    fun drawPlugin(c: Canvas, t: DeckTile, r: RectF, th: Theme, alpha: Int): Long {
        ink = th.text; sub = th.sub
        val v = PluginStore.get(t.id)
        val color = v?.color ?: t.color ?: th.accent
        frame(c, r, th, color, 0.1f, alpha)
        val w = r.width(); val h = r.height()
        val p = if (w < 200 * d) 14 * d else 18 * d
        caption(c, t.label ?: t.pv ?: I18n.t("Plugin"), r.left + p, r.top + p + 10 * d)
        val value = v?.v ?: "…"
        var size = min(h * 0.3f, 56 * d)
        text.typeface = medium
        while (size > 12 * d) { text.textSize = size; if (text.measureText(value) <= w - 2 * p) break; size *= 0.92f }
        val hasBar = v?.pct != null
        val cy = r.top + h / 2 + size * 0.3f - (if (hasBar) 4 * d else 0f)
        put(c, value, r.left + p, cy, size, ink, medium, maxW = w - 2 * p)
        v?.sub?.let { put(c, it, r.left + p, cy + 20 * d, 12 * d, sub, regular, maxW = w - 2 * p) }
        if (hasBar) {
            val y = r.bottom - p - 2 * d
            fill.color = withAlpha(sub, 0.15f); c.drawRect(r.left + p, y, r.right - p, y + 4 * d, fill)
            fill.color = color; c.drawRect(r.left + p, y, r.left + p + (w - 2 * p) * v!!.pct!!, y + 4 * d, fill)
        }
        return 0
    }

    fun drawWeather(c: Canvas, t: DeckTile, r: RectF, th: Theme, alpha: Int): Long {
        val spec = t.wx ?: return 0
        ink = th.text; sub = th.sub
        frame(c, r, th, t.color, 0.1f, alpha)
        val data = WeatherStore.data
        val p = if (r.width() < 200 * d) 14 * d else 18 * d
        if (data == null) {
            weatherIcon(c, "cloud", r.centerX(), r.centerY() - 14 * d, min(r.width(), r.height()) * 0.4f, alpha = 0.35f)
            put(c, I18n.t("No weather data"), r.centerX(), r.bottom - p - 14 * d, 13 * d, ink, medium, Paint.Align.CENTER, r.width() - 2 * p)
            put(c, I18n.t("Pick a city in MyDeck settings"), r.centerX(), r.bottom - p, 11 * d, sub, regular, Paint.Align.CENTER, r.width() - 2 * p)
            return 0
        }
        when (spec.view) {
            "tomorrow" -> tomorrow(c, t, r, data, p)
            "hours" -> if (r.height() >= 260 * d && r.width() >= 260 * d) hours(c, t, r, data, p) else now(c, t, r, data, p)
            else -> now(c, t, r, data, p)
        }
        return 33
    }

    private fun deg(v: Int?) = if (v == null) "–" else "$v°"

    private fun wk(r: RectF) = min(r.width() / (131 * d), r.height() / (183 * d)).coerceIn(0.8f, 1.5f)

    private fun now(c: Canvas, t: DeckTile, r: RectF, x: WeatherStore.Data, @Suppress("UNUSED_PARAMETER") p0: Float) {
        val n = x.now
        val w = r.width(); val h = r.height()
        val k = wk(r)
        val p = 14 * d * k
        caption(c, t.label ?: x.place.ifEmpty { I18n.t("Now") }, r.left + p, r.top + p + 10 * d * k, size = 11 * d * k,
            maxW = if (w <= h * 1.25f || w <= 200 * d) w - 2 * p else w * 0.6f)
        if (w <= h * 1.25f) {
            val big = min(h * 0.2f, w * 0.24f)
            weatherCardIcon(c, r, n.icon, p, k, big)
            put(c, deg(n.t), r.centerX(), r.bottom - p - 18 * d * k, big, ink, medium, Paint.Align.CENTER)
            put(c, n.cond, r.centerX(), r.bottom - p, 11.5f * d * k, sub, regular, Paint.Align.CENTER, w - 2 * p)
            return
        }

        val top = r.top + p + 16 * d * k
        val hh = r.bottom - p - top
        val s = min(hh / 0.79f, w * 0.45f)
        weatherIcon(c, n.icon, r.left + p + s / 2, top + hh / 2, s)
        val x0 = r.left + p + s + 12 * d * k
        val tw = r.right - p - x0
        val big = minOf(h * 0.3f, tw * 0.5f, 64 * d * k)
        val showCond = tw > 60 * d * k
        val extra = showCond && h > 140 * d * k && tw > 110 * d * k
        val lines = if (!showCond) 0 else if (extra) 2 else 1
        var y = top + hh / 2 - (big * 0.75f + lines * 18 * d * k) / 2 + big * 0.75f
        put(c, deg(n.t), x0, y, big, ink, light)
        if (showCond) { y += 18 * d * k; put(c, n.cond, x0, y, 13 * d * k, ink, medium, maxW = tw) }
        if (extra) { y += 18 * d * k; put(c, "Hissedilen ${deg(n.feels)} · Nem %${n.hum ?: "–"}", x0, y, 12 * d * k, sub, regular, maxW = tw) }
        if (w > 200 * d) put(c, "↑${deg(n.hi)}  ↓${deg(n.lo)}", r.right - p, r.top + p + 10 * d * k, 12 * d * k, sub, medium, Paint.Align.RIGHT)
    }

    private fun tomorrow(c: Canvas, t: DeckTile, r: RectF, x: WeatherStore.Data, @Suppress("UNUSED_PARAMETER") p0: Float) {
        val m = x.tomorrow
        val w = r.width(); val h = r.height()
        val k = wk(r)
        val p = 14 * d * k
        caption(c, t.label ?: m.day, r.left + p, r.top + p + 10 * d * k, size = 11 * d * k, maxW = w * 0.6f)
        val big = min(h * 0.14f, w * 0.15f)
        weatherCardIcon(c, r, m.icon, p, k, big)
        put(c, "${deg(m.hi)} / ${deg(m.lo)}", r.centerX(), r.bottom - p - 18 * d * k, big, ink, medium, Paint.Align.CENTER, w - 2 * p)
        put(c, if (m.rain != null) I18n.t("Rain {0}%", m.rain) else m.cond, r.centerX(), r.bottom - p, 11.5f * d * k, sub, regular, Paint.Align.CENTER, w - 2 * p)
    }

    private fun weatherCardIcon(c: Canvas, r: RectF, kind: String, p: Float, k: Float, big: Float) {
        val top = r.top + p + 14 * d * k
        val bottom = r.bottom - p - 18 * d * k - big * .80f
        val available = (bottom - top).coerceAtLeast(0f)
        val size = min(r.width() - 4 * d * k, available / .74f)
        weatherIcon(c, kind, r.centerX(), top + available / 2, size)
    }

    private fun hours(c: Canvas, t: DeckTile, r: RectF, x: WeatherStore.Data, p: Float) {
        val n = x.now
        val w = r.width(); val h = r.height()
        caption(c, t.label ?: x.place.ifEmpty { "Hava" }, r.left + p, r.top + p + 10 * d)
        if (x.at.isNotEmpty()) put(c, I18n.t("updated {0}", x.at), r.right - p, r.top + p + 10 * d, 12 * d, sub, regular, Paint.Align.RIGHT)
        val s = if (w < h * 1.3f) min(h * 0.46f, 145 * d) else min(h * 0.36f, 100 * d)
        val iy = r.top + p + 24 * d + s * 0.45f
        weatherIcon(c, n.icon, r.left + p + s * 0.5f, iy, s)
        val big = min(h * 0.24f, 64 * d)
        put(c, deg(n.t), r.left + p + s + 12 * d, iy + big * 0.36f, big, ink, light)
        put(c, "↑${deg(n.hi)}  ↓${deg(n.lo)}", r.right - p, iy + big * 0.36f, 13 * d, ink, medium, Paint.Align.RIGHT)
        val ty = iy + s * 0.5f + 26 * d
        put(c, n.cond, r.left + p, ty, 15 * d, ink, medium, maxW = w - 2 * p)
        put(c, I18n.t("Feels like {0}  ·  Humidity {1}%  ·  Wind {2} km/h", deg(n.feels), n.hum ?: "–", n.wind ?: "–"), r.left + p, ty + 20 * d, 12.5f * d, sub, regular, maxW = w - 2 * p)

        val y0 = ty + 36 * d
        val avail = r.bottom - p - y0
        if (avail < 70 * d || x.hours.isEmpty()) return
        fill.color = withAlpha(sub, 0.12f); c.drawRect(r.left + p, y0, r.right - p, y0 + d, fill)
        val count = min(x.hours.size, max(3, ((w - 2 * p) / (58 * d)).toInt()))
        val cw = (w - 2 * p) / count
        val isz = min(cw * 0.8f, avail * 0.45f)
        for (i in 0 until count) {
            val hr = x.hours[i]
            val cx = r.left + p + cw * i + cw / 2
            put(c, hr.h, cx, y0 + 18 * d, 12 * d, sub, regular, Paint.Align.CENTER)
            weatherIcon(c, hr.icon, cx, y0 + 18 * d + 8 * d + isz / 2, isz)
            put(c, deg(hr.t), cx, r.bottom - p, 14 * d, ink, medium, Paint.Align.CENTER)
        }
    }

    fun weatherIcon(c: Canvas, kind: String, cx: Float, cy: Float, s: Float, alpha: Float = 1f) {
        if (weatherArtwork.draw(c, kind, cx, cy, s, alpha, SystemClock.uptimeMillis())) return
        val sunC = Color.parseColor("#FFC53D")
        fun sun(x: Float, y: Float, r: Float) {
            fill.color = withAlpha(sunC, alpha); c.drawCircle(x, y, r, fill)
            stroke.color = withAlpha(sunC, alpha); stroke.strokeWidth = max(1.5f, r * 0.18f); stroke.strokeCap = Paint.Cap.ROUND
            for (i in 0 until 8) {
                val a = i * PI / 4
                c.drawLine(x + (cos(a) * r * 1.35f).toFloat(), y + (sin(a) * r * 1.35f).toFloat(),
                    x + (cos(a) * r * 1.75f).toFloat(), y + (sin(a) * r * 1.75f).toFloat(), stroke)
            }
            stroke.strokeCap = Paint.Cap.BUTT
        }
        fun moon(x: Float, y: Float, r: Float) {
            path.reset()
            path.addCircle(x, y, r, Path.Direction.CW)
            val cut = Path().apply { addCircle(x + r * 0.45f, y - r * 0.3f, r * 0.85f, Path.Direction.CW) }
            path.op(cut, Path.Op.DIFFERENCE)
            fill.color = withAlpha(Color.parseColor("#E8E2C8"), alpha); c.drawPath(path, fill)
        }
        fun cloud(x: Float, y: Float, w: Float, col: Int) {
            fill.color = withAlpha(col, alpha)
            path.reset()
            path.addCircle(x - w * 0.22f, y + w * 0.04f, w * 0.2f, Path.Direction.CW)
            path.addCircle(x + w * 0.02f, y - w * 0.08f, w * 0.27f, Path.Direction.CW)
            path.addCircle(x + w * 0.26f, y + w * 0.05f, w * 0.19f, Path.Direction.CW)
            rect.set(x - w * 0.42f, y + w * 0.02f, x + w * 0.44f, y + w * 0.24f)
            path.addRoundRect(rect, w * 0.11f, w * 0.11f, Path.Direction.CW)
            c.drawPath(path, fill)
        }
        fun drops(col: Int, slant: Boolean, n: Int = 3) {
            stroke.color = withAlpha(col, alpha); stroke.strokeWidth = max(1.5f, s * 0.04f); stroke.strokeCap = Paint.Cap.ROUND
            for (i in 0 until n) {
                val ox = (i - (n - 1) / 2f) * s * 0.18f
                c.drawLine(cx + ox, cy + s * 0.22f, cx + ox - (if (slant) s * 0.06f else 0f), cy + s * 0.36f, stroke)
            }
            stroke.strokeCap = Paint.Cap.BUTT
        }
        fun flakes(n: Int = 3) {
            fill.color = withAlpha(Color.WHITE, alpha)
            for (i in 0 until n) c.drawCircle(cx + (i - (n - 1) / 2f) * s * 0.18f, cy + s * 0.3f + (if (i % 2 == 0) 0f else s * 0.06f), max(1.5f, s * 0.035f), fill)
        }
        when (kind) {
            "sun" -> sun(cx, cy, s * 0.22f)
            "moon" -> moon(cx, cy, s * 0.24f)
            "cloudsun" -> { sun(cx - s * 0.14f, cy - s * 0.14f, s * 0.17f); cloud(cx + s * 0.06f, cy + s * 0.08f, s * 0.8f, Color.parseColor("#D9E0EA")) }
            "cloudmoon" -> { moon(cx - s * 0.14f, cy - s * 0.16f, s * 0.19f); cloud(cx + s * 0.06f, cy + s * 0.08f, s * 0.8f, Color.parseColor("#C9D2DE")) }
            "rain" -> { cloud(cx, cy - s * 0.08f, s * 0.8f, Color.parseColor("#AFC0D3")); drops(Color.parseColor("#3CC8F0"), true) }
            "sleet" -> { cloud(cx, cy - s * 0.08f, s * 0.8f, Color.parseColor("#AFC0D3")); drops(Color.parseColor("#3CC8F0"), true, 2); flakes(1) }
            "snow" -> { cloud(cx, cy - s * 0.08f, s * 0.8f, Color.parseColor("#C9D2DE")); flakes() }
            "storm" -> {
                cloud(cx, cy - s * 0.1f, s * 0.8f, Color.parseColor("#8E9BB0"))
                path.reset()
                path.moveTo(cx + s * 0.04f, cy + s * 0.12f); path.lineTo(cx - s * 0.1f, cy + s * 0.3f); path.lineTo(cx, cy + s * 0.3f)
                path.lineTo(cx - s * 0.06f, cy + s * 0.44f); path.lineTo(cx + s * 0.12f, cy + s * 0.24f); path.lineTo(cx + s * 0.02f, cy + s * 0.24f); path.close()
                fill.color = withAlpha(Color.parseColor("#FFC53D"), alpha); c.drawPath(path, fill)
            }
            "fog" -> {
                cloud(cx, cy - s * 0.12f, s * 0.72f, Color.parseColor("#9AA6B8"))
                stroke.color = withAlpha(Color.parseColor("#C9D2DE"), alpha); stroke.strokeWidth = max(1.5f, s * 0.05f); stroke.strokeCap = Paint.Cap.ROUND
                c.drawLine(cx - s * 0.32f, cy + s * 0.2f, cx + s * 0.28f, cy + s * 0.2f, stroke)
                c.drawLine(cx - s * 0.24f, cy + s * 0.32f, cx + s * 0.34f, cy + s * 0.32f, stroke)
                stroke.strokeCap = Paint.Cap.BUTT
            }
            "wind" -> {
                stroke.color = withAlpha(Color.parseColor("#C9D2DE"), alpha); stroke.strokeWidth = max(1.5f, s * 0.05f); stroke.strokeCap = Paint.Cap.ROUND
                stroke.style = Paint.Style.STROKE
                for ((i, len) in listOf(0.6f, 0.75f, 0.5f).withIndex()) {
                    val y = cy - s * 0.16f + i * s * 0.16f
                    c.drawLine(cx - s * 0.36f, y, cx - s * 0.36f + s * len, y, stroke)
                }
                stroke.strokeCap = Paint.Cap.BUTT
            }
            else -> cloud(cx, cy, s * 0.85f, Color.parseColor("#C9D2DE"))
        }
    }

    fun drawPulse(c: Canvas, r: RectF, th: Theme, color: Int): Long {
        val now = SystemClock.uptimeMillis()
        c.save()
        path.reset(); path.addRoundRect(r, th.radius, th.radius, Path.Direction.CW)
        c.clipPath(path)
        val cx = r.centerX(); val cy = r.top + r.height() * 0.42f
        val maxR = max(r.width(), r.height()) * 0.62f
        stroke.strokeWidth = 2 * d
        for (i in 0 until 3) {
            val k = ((now / 1600f) + i / 3f) % 1f
            stroke.color = withAlpha(color, 0.5f * (1 - k))
            c.drawCircle(cx, cy, maxR * (0.25f + 0.75f * k), stroke)
        }
        fill.shader = RadialGradient(cx, cy, maxR, withAlpha(color, 0.10f + 0.06f * sin(now / 500.0).toFloat()), withAlpha(color, 0f), Shader.TileMode.CLAMP)
        c.drawRoundRect(r, th.radius, th.radius, fill)
        fill.shader = null
        c.restore()
        return 40
    }

    private fun mix(a: Int, b: Int, k: Float): Int = Color.rgb(
        (Color.red(a) + (Color.red(b) - Color.red(a)) * k).toInt(),
        (Color.green(a) + (Color.green(b) - Color.green(a)) * k).toInt(),
        (Color.blue(a) + (Color.blue(b) - Color.blue(a)) * k).toInt(),
    )

    private fun withAlpha(c: Int, a: Float): Int = Color.argb((a.coerceIn(0f, 1f) * Color.alpha(c)).toInt(), Color.red(c), Color.green(c), Color.blue(c))

    @Suppress("unused") private fun near(a: Float, b: Float) = abs(a - b) < 0.5f
}
