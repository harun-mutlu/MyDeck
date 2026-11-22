package app.mydeck

import android.graphics.Bitmap
import android.graphics.BitmapShader
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.LinearGradient
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RadialGradient
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import android.os.SystemClock
import java.util.Locale
import kotlin.math.hypot
import kotlin.math.max
import kotlin.math.min

class MediaRenderer(private val d: Float) {
    class Hit(val id: String, val cx: Float, val cy: Float, val r: Float, val x0: Float = 0f, val x1: Float = 0f)

    private val hits = HashMap<String, ArrayList<Hit>>()
    private val pressed = HashMap<String, Long>()

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val artPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val text = Paint(Paint.ANTI_ALIAS_FLAG)
    private val regular = Typeface.create("sans-serif", Typeface.NORMAL)
    private val medium = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    private val bold = Typeface.create("sans-serif", Typeface.BOLD)
    private val path = Path()
    private val rect = RectF()
    private val m = Matrix()
    private val shaders = HashMap<String, Shader>()
    private val artShaders = HashMap<Bitmap, BitmapShader>()
    private val TR: Locale get() = if (I18n.lang == "tr") Locale("tr", "TR") else Locale.US

    private var ink = 0
    private var sub = 0
    private var track = 0

    fun draw(c: Canvas, t: DeckTile, r: RectF, th: Theme, alpha: Int = 255): Boolean {
        val spec = t.media ?: return false
        val info = MediaStore.get(spec.key)
        ink = th.text; sub = th.sub; track = withAlpha(th.sub, 0.16f)
        val list = hits.getOrPut(t.id) { ArrayList() }
        list.clear()
        val tint = if (spec.followArt && info != null && !info.empty) info.tint else (t.color ?: th.accent)
        frame(c, r, th, tint, alpha)

        if (info == null || info.empty) {
            empty(c, r)
            return false
        }
        val now = SystemClock.uptimeMillis()
        val fade = (MediaStore.artAge(spec.key) / 350f).coerceIn(0f, 1f)
        val prev = if (fade < 1f) MediaStore.previousArt(spec.key) else null
        val w = r.width(); val h = r.height()
        val small = w < 200 * d; val short = h < 200 * d

        when {
            t.id == "yenisayfa-4d2509" -> customPlayer(c, t, r, info, prev, fade, spec, list, tint)
            small && short -> cover(c, t, r, th, info, prev, fade, spec, list, tint)
            short -> strip(c, t, r, info, prev, fade, spec, list, tint)
            small -> tall(c, t, r, th, info, prev, fade, spec, list, tint)
            w >= h * 1.55f -> player(c, t, r, info, prev, fade, spec, list, tint)
            else -> square(c, t, r, info, prev, fade, spec, list, tint)
        }
        return fade < 1f || pressed.values.any { now - it < 160 }
    }

    fun hit(t: DeckTile, x: Float, y: Float): Pair<String, Float>? {
        val list = hits[t.id] ?: return null
        for (h in list) {
            if (h.id == "seek") {
                if (x >= h.x0 - 6 * d && x <= h.x1 + 6 * d && kotlin.math.abs(y - h.cy) <= 16 * d) {
                    val info = MediaStore.get(t.media?.key ?: "") ?: return null
                    if (!info.canSeek || info.dur <= 0f) return null
                    return "seek" to ((x - h.x0) / (h.x1 - h.x0)).coerceIn(0f, 1f) * info.dur
                }
            } else if (hypot(x - h.cx, y - h.cy) <= h.r) {
                pressed["${t.id}|${h.id}"] = SystemClock.uptimeMillis()
                return h.id to 0f
            }
        }
        return null
    }

    private fun cover(c: Canvas, t: DeckTile, r: RectF, th: Theme, info: MediaStore.Info, prev: Bitmap?, fade: Float,
                      spec: MediaSpec, list: ArrayList<Hit>, tint: Int) {
        val k = (r.height() / (120 * d)).coerceIn(0.7f, 1.2f)
        val side = max(r.width(), r.height())
        rect.set(r.centerX() - side / 2, r.centerY() - side / 2, r.centerX() + side / 2, r.centerY() + side / 2)
        artInto(c, info.art, prev, fade, rect, r, th.radius)

        fill.shader = shader("cover|${r.top.toInt()}|${r.bottom.toInt()}") {
            LinearGradient(0f, r.top + r.height() * 0.35f, 0f, r.bottom, Color.argb(0, 7, 9, 14), Color.argb(220, 7, 9, 14), Shader.TileMode.CLAMP)
        }
        c.drawRoundRect(r, th.radius, th.radius, fill)
        fill.shader = null
        val cx = r.centerX(); val cy = r.centerY() - 8 * d * k
        fill.color = Color.argb(115, 7, 9, 14)
        c.drawCircle(cx, cy, 24 * d * k * press(t, "play"), fill)
        if (info.playing) pause(c, cx, cy, 22 * d * k, ink) else play(c, cx + 2 * d * k, cy, 22 * d * k, ink)
        list.add(Hit("play", r.centerX(), r.centerY(), max(r.width(), r.height())))
        text.typeface = medium; text.textSize = max(11 * d, 12 * d * k); text.color = ink; text.textAlign = Paint.Align.LEFT
        c.drawText(ellipsize(info.title, r.width() - 24 * d * k), r.left + 12 * d * k, r.bottom - 14 * d * k, text)
        edgeBar(c, r, th.radius, info, tint, k)
    }

    private fun customPlayer(c: Canvas, t: DeckTile, r: RectF, info: MediaStore.Info, prev: Bitmap?, fade: Float,
                      spec: MediaSpec, list: ArrayList<Hit>, tint: Int) {
        val p = 12 * d
        val barY = r.bottom - (if (spec.times) 26 else 12) * d
        val size = min(29 * d, r.height() * 0.18f)
        val cy = barY - size - 18 * d
        val a = r.height() - 2 * p
        rect.set(r.left + p, r.top + p, r.left + p + a, r.top + p + a)
        artInto(c, info.art, prev, fade, rect, rect, 10 * d)
        val controlsX = (rect.right + r.right) / 2
        val gap = min(72 * d, (r.right - rect.right) / 3)
        val x0 = rect.right + 16 * d
        val iw = r.right - p - x0
        text.textAlign = Paint.Align.LEFT
        text.typeface = medium; text.textSize = 16 * d; text.color = ink
        c.drawText(ellipsize(info.title, iw), x0, r.top + 20 * d, text)
        if (!spec.controls || cy - size >= r.top + 42 * d) {
            text.typeface = regular; text.textSize = 12.5f * d; text.color = sub
            c.drawText(ellipsize(info.artist, iw), x0, r.top + 38 * d, text)
        }
        if (spec.controls) controls(c, t, info, controlsX, cy, size, gap, list)
        progress(c, info, x0, barY, iw, spec.times, list, tint)
    }

    private fun player(c: Canvas, t: DeckTile, r: RectF, info: MediaStore.Info, prev: Bitmap?, fade: Float,
                       spec: MediaSpec, list: ArrayList<Hit>, tint: Int) {
        val k = (r.height() / (183 * d)).coerceIn(0.7f, 1.5f)
        val p = 12 * d * k
        val barY = r.bottom - (if (spec.times) 26 else 12) * d * k
        val size = min(29 * d * k, r.height() * 0.18f)
        val cy = barY - size - 18 * d * k
        val a = min(r.height() - 2 * p, r.width() - 2 * p - 186 * d * k).coerceAtLeast(0f)
        val ay = r.top + (r.height() - a) / 2
        rect.set(r.left + p, ay, r.left + p + a, ay + a)
        artInto(c, info.art, prev, fade, rect, rect, 10 * d * k)
        val artRight = rect.right
        val x0 = artRight + 16 * d * k
        val iw = r.right - p - x0
        text.textAlign = Paint.Align.LEFT
        text.typeface = medium; text.textSize = 16 * d * k; text.color = ink
        c.drawText(ellipsize(info.title, iw), x0, r.top + 20 * d * k, text)
        if (!spec.controls || cy - size >= r.top + 42 * d * k) {
            text.typeface = regular; text.textSize = 12.5f * d * k; text.color = sub
            c.drawText(ellipsize(info.artist, iw), x0, r.top + 38 * d * k, text)
        }
        if (spec.controls) controls(c, t, info, (artRight + r.right) / 2, cy, size, min(72 * d * k, (r.right - artRight) / 3), list)
        progress(c, info, x0, barY, iw, spec.times, list, tint, k)
    }

    private fun square(c: Canvas, t: DeckTile, r: RectF, info: MediaStore.Info, prev: Bitmap?, fade: Float,
                       spec: MediaSpec, list: ArrayList<Hit>, tint: Int) {
        val k = min(r.height() / (183 * d), r.width() / (276 * d)).coerceIn(0.7f, 1.5f)
        val p = 12 * d * k
        val barY = r.bottom - (if (spec.times) 26 else 12) * d * k
        val size = min(29 * d * k, r.height() * 0.16f)
        val cy = barY - size - 18 * d * k
        val ctlTop = if (spec.controls) cy - size - 12 * d * k else barY - 20 * d * k
        val a = minOf(r.height() * 0.5f, r.width() * 0.42f, ctlTop - (r.top + p)).coerceAtLeast(0f)
        rect.set(r.left + p, r.top + p, r.left + p + a, r.top + p + a)
        artInto(c, info.art, prev, fade, rect, rect, 10 * d * k)
        val x0 = r.left + p + a + 14 * d * k
        val iw = r.right - p - x0
        val ty = r.top + p + max(16 * d * k, (a - 30 * d * k) / 2 + 14 * d * k)
        text.textAlign = Paint.Align.LEFT
        text.typeface = medium; text.textSize = 16 * d * k; text.color = ink
        c.drawText(ellipsize(info.title, iw), x0, ty, text)
        text.typeface = regular; text.textSize = 12.5f * d * k; text.color = sub
        c.drawText(ellipsize(info.artist, iw), x0, ty + 18 * d * k, text)
        if (spec.controls) controls(c, t, info, r.centerX(), cy, size, min(72 * d * k, (r.width() - 2 * p) / 4), list)
        progress(c, info, r.left + p, barY, r.width() - 2 * p, spec.times, list, tint, k)
    }

    private fun tall(c: Canvas, t: DeckTile, r: RectF, th: Theme, info: MediaStore.Info, prev: Bitmap?, fade: Float,
                     spec: MediaSpec, list: ArrayList<Hit>, tint: Int) {
        val k = min(r.width() / (150 * d), r.height() / (183 * d)).coerceIn(0.7f, 1.3f)
        val p = 12 * d * k
        val size = min(29 * d * k, r.width() * 0.13f)
        val cy = r.bottom - p - size - 4 * d * k
        val artistY = if (spec.controls) cy - size - 12 * d * k else r.bottom - p - 8 * d * k
        val titleY = artistY - 16 * d * k
        val a = min(r.width() - 2 * p, titleY - 18 * d * k - (r.top + p)).coerceAtLeast(0f)
        rect.set(r.centerX() - a / 2, r.top + p, r.centerX() + a / 2, r.top + p + a)
        artInto(c, info.art, prev, fade, rect, rect, 10 * d * k)
        text.textAlign = Paint.Align.CENTER
        text.typeface = medium; text.textSize = 15 * d * k; text.color = ink
        c.drawText(ellipsize(info.title, r.width() - 2 * p), r.centerX(), titleY, text)
        text.typeface = regular; text.textSize = 12 * d * k; text.color = sub
        c.drawText(ellipsize(info.artist, r.width() - 2 * p), r.centerX(), artistY, text)
        text.textAlign = Paint.Align.LEFT
        if (spec.controls) controls(c, t, info, r.centerX(), cy, size, min(44 * d * k, (r.width() - 2 * p) / 2.6f), list)
        edgeBar(c, r, th.radius, info, tint, k)
    }

    private fun strip(c: Canvas, t: DeckTile, r: RectF, info: MediaStore.Info, prev: Bitmap?, fade: Float,
                      spec: MediaSpec, list: ArrayList<Hit>, tint: Int) {
        val k = (r.height() / (110 * d)).coerceIn(0.7f, 1.2f)
        val p = 10 * d * k
        val a = r.height() - 2 * p
        rect.set(r.left + p, r.top + p, r.left + p + a, r.top + p + a)
        artInto(c, info.art, prev, fade, rect, rect, 8 * d * k)
        val x0 = rect.right + 12 * d * k
        val right = r.right - p
        val size = min(29 * d * k, r.height() * 0.2f)
        val gap = 44 * d * k
        val ctlW = if (spec.controls) 2 * gap + 2 * size else 0f
        val room = right - x0
        val inline = !spec.controls || room - ctlW > 150 * d * k
        val iw = if (spec.controls && inline) room - ctlW - 12 * d * k else room
        text.textAlign = Paint.Align.LEFT
        text.typeface = medium; text.textSize = max(13 * d, 16 * d * k); text.color = ink
        c.drawText(ellipsize(info.title, iw), x0, r.top + p + 16 * d * k, text)
        text.typeface = regular; text.textSize = max(11 * d, 12.5f * d * k); text.color = sub
        c.drawText(ellipsize(info.artist, iw), x0, r.top + p + 33 * d * k, text)
        if (inline) {
            if (spec.controls) controls(c, t, info, right - gap - size * 0.6f, r.centerY() - 4 * d * k, size, gap, list)
            thinBar(c, info, x0, r.bottom - p - 4 * d * k, iw, tint, k)
        } else {
            val s2 = size * 0.85f
            val by = r.bottom - p - s2
            val px = x0 + s2
            c.save(); c.translate(px, by); val kp = press(t, "play"); c.scale(kp, kp)
            fill.color = ink; c.drawCircle(0f, 0f, s2, fill)
            if (info.playing) pause(c, 0f, 0f, s2 * 0.85f, Color.rgb(11, 14, 20)) else play(c, 1.5f * d * k, 0f, s2 * 0.85f, Color.rgb(11, 14, 20))
            c.restore()
            list.add(Hit("play", px, by, s2 + 8 * d * k))
            val nx = x0 + 2 * s2 + 20 * d * k
            c.save(); c.translate(nx, by); val kn = press(t, "next"); c.scale(kn, kn)
            skip(c, 0f, 0f, s2 * 0.85f, if (info.canNext) ink else withAlpha(ink, 0.35f), 1f)
            c.restore()
            list.add(Hit("next", nx, by, max(18 * d * k, s2)))
            val bx = x0 + 2 * s2 + 40 * d * k
            thinBar(c, info, bx, by - 2 * d * k, right - bx, tint, k)
        }
    }

    private fun empty(c: Canvas, r: RectF) {
        val cx = r.centerX(); val cy = r.centerY() - 10 * d

        fill.color = withAlpha(sub, 0.6f)
        c.drawCircle(cx - 6 * d, cy + 8 * d, 6 * d, fill)
        rect.set(cx - 1 * d, cy - 14 * d, cx + 1.5f * d, cy + 8 * d); c.drawRect(rect, fill)
        rect.set(cx - 1 * d, cy - 14 * d, cx + 10 * d, cy - 10 * d); c.drawRoundRect(rect, 2 * d, 2 * d, fill)
        text.typeface = regular; text.textSize = 12 * d; text.color = sub; text.textAlign = Paint.Align.CENTER
        c.drawText(ellipsize(I18n.t("Nothing is playing"), r.width() - 20 * d), cx, cy + 34 * d, text)
        text.textAlign = Paint.Align.LEFT
    }

    private fun frame(c: Canvas, r: RectF, th: Theme, tint: Int, alpha: Int) {
        fill.shader = null
        fill.color = mix(th.tile, tint, 0.13f)

        fill.alpha = alpha
        c.drawRoundRect(r, th.radius, th.radius, fill)
        fill.alpha = 255
        fill.shader = shader("glow|$tint|${r.left.toInt()}|${r.top.toInt()}|${r.width().toInt()}") {
            RadialGradient(r.left + r.width() * 0.15f, r.top, max(r.width(), r.height()) * 0.9f,
                withAlpha(tint, 0.22f), withAlpha(tint, 0f), Shader.TileMode.CLAMP)
        }
        c.drawRoundRect(r, th.radius, th.radius, fill)
        fill.shader = null
        stroke.strokeWidth = max(1f, d); stroke.color = mix(th.border, tint, 0.35f)
        val h = stroke.strokeWidth / 2
        rect.set(r.left + h, r.top + h, r.right - h, r.bottom - h)
        c.drawRoundRect(rect, th.radius - h, th.radius - h, stroke)
    }

    private fun artInto(c: Canvas, art: Bitmap?, prev: Bitmap?, fade: Float, dst: RectF, clip: RectF, radius: Float) {
        val box = RectF(dst)
        if (clip !== dst) { box.intersect(clip) }
        if (art == null && prev == null) {
            fill.color = withAlpha(sub, 0.12f); c.drawRoundRect(box, radius, radius, fill)
            return
        }
        if (art != null) bitmapRound(c, art, dst, box, radius, 255)
        if (prev != null && !prev.isRecycled) bitmapRound(c, prev, dst, box, radius, ((1f - fade) * 255).toInt())
        stroke.strokeWidth = max(1f, d); stroke.color = Color.argb(16, 255, 255, 255)
        c.drawRoundRect(box, radius, radius, stroke)
    }

    private fun bitmapRound(c: Canvas, b: Bitmap, dst: RectF, box: RectF, radius: Float, alpha: Int) {
        val sh = artShaders.getOrPut(b) {
            if (artShaders.size > 8) artShaders.clear()
            BitmapShader(b, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP)
        }
        val side = min(b.width, b.height).toFloat()
        val s = dst.width() / side
        m.reset()
        m.setScale(s, s)
        m.postTranslate(dst.left - (b.width - side) / 2 * s, dst.top - (b.height - side) / 2 * s)
        sh.setLocalMatrix(m)
        artPaint.shader = sh
        artPaint.alpha = alpha
        c.drawRoundRect(box, radius, radius, artPaint)
        artPaint.shader = null
    }

    private fun controls(c: Canvas, t: DeckTile, info: MediaStore.Info, cx: Float, cy: Float, size: Float, gap: Float, list: ArrayList<Hit>) {
        val sk = size * 0.85f
        val dim = withAlpha(ink, 0.35f)
        c.save(); c.translate(cx - gap, cy); val kp = press(t, "prev"); c.scale(kp, kp)
        skip(c, 0f, 0f, sk, if (info.canPrev) ink else dim, -1f); c.restore()
        c.save(); c.translate(cx + gap, cy); val kn = press(t, "next"); c.scale(kn, kn)
        skip(c, 0f, 0f, sk, if (info.canNext) ink else dim, 1f); c.restore()
        c.save(); c.translate(cx, cy); val k = press(t, "play"); c.scale(k, k)
        fill.color = ink; c.drawCircle(0f, 0f, size, fill)
        if (info.playing) pause(c, 0f, 0f, size * 0.85f, Color.rgb(11, 14, 20)) else play(c, 2 * d, 0f, size * 0.85f, Color.rgb(11, 14, 20))
        c.restore()
        list.add(Hit("prev", cx - gap, cy, max(24 * d, gap * 0.48f)))
        list.add(Hit("play", cx, cy, size + 8 * d))
        list.add(Hit("next", cx + gap, cy, max(24 * d, gap * 0.48f)))
    }

    private fun progress(c: Canvas, info: MediaStore.Info, x: Float, y: Float, w: Float, times: Boolean, list: ArrayList<Hit>, tint: Int, k: Float = 1f) {
        val f = frac(info)

        val bar = vivid(tint)
        val t = 3 * d * k
        fill.color = withAlpha(Color.WHITE, 0.14f)
        rect.set(x, y - t, x + w, y + t); c.drawRoundRect(rect, t, t, fill)
        fill.color = bar
        rect.set(x, y - t, x + max(2 * t, w * f), y + t); c.drawRoundRect(rect, t, t, fill)
        if (info.canSeek) { fill.color = Color.WHITE; c.drawCircle(x + w * f, y, 7 * d * k, fill); fill.color = bar; c.drawCircle(x + w * f, y, 3 * d * k, fill) }
        if (times && info.dur > 0) {
            text.typeface = regular; text.textSize = 11 * d * k; text.color = sub; text.textAlign = Paint.Align.LEFT
            val pos = info.position()
            c.drawText(time(pos), x, y + 20 * d * k, text)
            text.textAlign = Paint.Align.RIGHT
            c.drawText("-" + time(info.dur - pos), x + w, y + 20 * d * k, text)
            text.textAlign = Paint.Align.LEFT
        }
        list.add(Hit("seek", 0f, y, 0f, x, x + w))
    }

    private fun thinBar(c: Canvas, info: MediaStore.Info, x: Float, y: Float, w: Float, tint: Int, k: Float = 1f) {
        if (w <= 0) return
        val h = 4 * d * k
        fill.color = withAlpha(Color.WHITE, 0.14f)
        rect.set(x, y, x + w, y + h); c.drawRoundRect(rect, h / 2, h / 2, fill)
        fill.color = vivid(tint)
        rect.set(x, y, x + max(h, w * frac(info)), y + h); c.drawRoundRect(rect, h / 2, h / 2, fill)
    }

    private fun edgeBar(c: Canvas, r: RectF, radius: Float, info: MediaStore.Info, tint: Int, k: Float) {
        val h = 3 * d * k
        c.save()
        c.clipRect(r.left, r.bottom - h, r.right, r.bottom)
        fill.shader = null
        fill.color = Color.argb(38, 255, 255, 255); c.drawRoundRect(r, radius, radius, fill)
        c.clipRect(r.left, r.bottom - h, r.left + r.width() * frac(info), r.bottom)
        fill.color = vivid(tint); c.drawRoundRect(r, radius, radius, fill)
        c.restore()
    }

    private fun play(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        fill.color = color
        path.reset()
        path.moveTo(cx - s * 0.32f, cy - s * 0.42f); path.lineTo(cx + s * 0.46f, cy); path.lineTo(cx - s * 0.32f, cy + s * 0.42f); path.close()
        c.drawPath(path, fill)
    }

    private fun pause(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        fill.color = color
        rect.set(cx - s * 0.36f, cy - s * 0.4f, cx - s * 0.12f, cy + s * 0.4f); c.drawRoundRect(rect, s * 0.06f, s * 0.06f, fill)
        rect.set(cx + s * 0.12f, cy - s * 0.4f, cx + s * 0.36f, cy + s * 0.4f); c.drawRoundRect(rect, s * 0.06f, s * 0.06f, fill)
    }

    private fun skip(c: Canvas, cx: Float, cy: Float, s: Float, color: Int, dir: Float) {
        c.save(); c.translate(cx, cy); c.scale(dir, 1f)
        fill.color = color
        path.reset()
        path.moveTo(-s * 0.4f, -s * 0.36f); path.lineTo(s * 0.22f, 0f); path.lineTo(-s * 0.4f, s * 0.36f); path.close()
        c.drawPath(path, fill)
        rect.set(s * 0.24f, -s * 0.36f, s * 0.38f, s * 0.36f); c.drawRoundRect(rect, s * 0.04f, s * 0.04f, fill)
        c.restore()
    }

    private fun press(t: DeckTile, id: String): Float {
        val at = pressed["${t.id}|$id"] ?: return 1f
        val k = (SystemClock.uptimeMillis() - at) / 160f
        return if (k >= 1f) 1f else 0.88f + 0.12f * k
    }

    private fun vivid(tint: Int): Int {
        val hsv = FloatArray(3)
        Color.colorToHSV(tint, hsv)
        if (hsv[1] < 0.25f) return Color.parseColor("#FF3D5A")
        hsv[1] = max(hsv[1], 0.6f); hsv[2] = max(hsv[2], 0.9f)
        return Color.HSVToColor(hsv)
    }

    private fun frac(info: MediaStore.Info) = if (info.dur > 0) (info.position() / info.dur).coerceIn(0f, 1f) else 0f

    private fun time(s: Float): String {
        val t = max(0, s.toInt())
        return if (t >= 3600) String.format(TR, "%d:%02d:%02d", t / 3600, t / 60 % 60, t % 60) else String.format(TR, "%d:%02d", t / 60, t % 60)
    }

    private fun ellipsize(s: String, maxW: Float): String {
        if (text.measureText(s) <= maxW) return s
        var e = s
        while (e.length > 1 && text.measureText("$e…") > maxW) e = e.dropLast(1)
        return "$e…"
    }

    private fun wrap2(s: String, w: Float): List<String> {
        val words = s.split(" ")
        var a = ""
        for (i in words.indices) {
            val t = if (a.isEmpty()) words[i] else "$a ${words[i]}"
            if (text.measureText(t) > w && a.isNotEmpty()) return listOf(a, ellipsize(words.drop(i).joinToString(" "), w))
            a = t
        }
        return listOf(ellipsize(a, w))
    }

    private inline fun shader(key: String, make: () -> Shader): Shader {
        shaders[key]?.let { return it }
        if (shaders.size > 48) shaders.clear()
        return make().also { shaders[key] = it }
    }

    private fun mix(a: Int, b: Int, k: Float): Int = Color.rgb(
        (Color.red(a) + (Color.red(b) - Color.red(a)) * k).toInt(),
        (Color.green(a) + (Color.green(b) - Color.green(a)) * k).toInt(),
        (Color.blue(a) + (Color.blue(b) - Color.blue(a)) * k).toInt(),
    )

    private fun withAlpha(c: Int, a: Float): Int = Color.argb((a.coerceIn(0f, 1f) * 255).toInt(), Color.red(c), Color.green(c), Color.blue(c))
}
