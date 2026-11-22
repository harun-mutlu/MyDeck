package app.mydeck

import android.graphics.Canvas
import android.graphics.Color
import android.graphics.DashPathEffect
import android.graphics.LinearGradient
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import android.graphics.Shader
import android.graphics.Typeface
import java.util.Locale
import kotlin.math.cos
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

class LiveRenderer(private val d: Float) {
    companion object {
        val KINDS = setOf("graph", "ring", "gauge", "net", "procs")
        private val TR: Locale get() = if (I18n.lang == "tr") Locale("tr", "TR") else Locale.US
    }

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE; strokeJoin = Paint.Join.ROUND; strokeCap = Paint.Cap.ROUND
    }
    private val text = Paint(Paint.ANTI_ALIAS_FLAG)
    private val regular = Typeface.create("sans-serif", Typeface.NORMAL)
    private val medium = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    private val bold = Typeface.create("sans-serif", Typeface.BOLD)
    private val line = Path()
    private val area = Path()
    private val mask = Path()
    private val dash = DashPathEffect(floatArrayOf(4 * d, 4 * d), 0f)
    private val g = RectF()
    private val oval = RectF()
    private val shaders = HashMap<String, Shader>()
    private val autoMax = HashMap<String, Float>()
    private val smooth = HashMap<String, Float>()

    private var sub = 0
    private var ink = 0
    private var track = 0
    private var grid = 0
    private var bg = 0

    private class Plot(val s: Series, val mirror: Boolean = false, val down: Boolean = false)

    fun draw(c: Canvas, t: DeckTile, r: RectF, th: Theme, alpha: Int = 255) {
        val spec = t.live ?: return
        sub = th.sub; ink = th.text; bg = th.bg
        track = withAlpha(th.sub, 0.10f)
        grid = withAlpha(th.sub, 0.09f)
        val glass = alpha < 255
        c.save()
        if (glass) {
            mask.reset()
            mask.addRoundRect(r, th.radius, th.radius, Path.Direction.CW)
            c.clipPath(mask)
        } else c.clipRect(r)
        when (t.kind) {
            "graph" -> graph(c, t, spec, r)
            "ring" -> ring(c, t, spec, r)
            "gauge" -> gauge(c, t, spec, r)
            "net" -> net(c, t, spec, r)
            "procs" -> procs(c, t, r)
        }
        c.restore()
        if (glass) {
            stroke.pathEffect = null
            stroke.strokeWidth = max(1f, d)
            stroke.color = th.border
            stroke.alpha = (90 + alpha * 0.6f).toInt().coerceAtMost(255)
            val hh = stroke.strokeWidth / 2
            oval.set(r.left + hh, r.top + hh, r.right - hh, r.bottom - hh)
            c.drawRoundRect(oval, th.radius - hh, th.radius - hh, stroke)
            stroke.alpha = 255
            return
        }

        mask.reset()
        mask.fillType = Path.FillType.EVEN_ODD
        mask.addRect(r, Path.Direction.CW)
        mask.addRoundRect(r, th.radius, th.radius, Path.Direction.CW)
        fill.shader = null; fill.color = th.bg
        c.drawPath(mask, fill)
        stroke.pathEffect = null
        stroke.strokeWidth = max(1f, d)
        stroke.color = th.border
        val h = stroke.strokeWidth / 2
        oval.set(r.left + h, r.top + h, r.right - h, r.bottom - h)
        c.drawRoundRect(oval, th.radius - h, th.radius - h, stroke)
    }

    private fun graph(c: Canvas, t: DeckTile, spec: LiveSpec, r: RectF) {
        val s0 = spec.series.firstOrNull() ?: return
        val plots = spec.series.map { Plot(it) }
        val p = 14 * d
        val w = r.width(); val h = r.height()
        val small = w < 200 * d; val short = h < 200 * d
        val title = t.label ?: s0.name
        val value = valueText(s0)
        val unit = unitText(s0)
        if (plots.size == 1) {
            when {
                h < 130 * d -> graphStrip(c, t, spec, plots, r, title, value, unit)
                w < 200 * d -> graphTall(c, t, spec, plots, r, title, value, unit)
                else -> graphFull(c, t, spec, plots, r, title, value, unit)
            }
            return
        }
        when {
            small && short -> {
                label(c, title, r.left + p, r.top + 24 * d)
                big(c, value, unit, r.left + p, r.top + 66 * d, 30 * d)
                g.set(r.left, r.bottom - 50 * d, r.right, r.bottom)
                series(c, t.id, spec, plots, flush = true, gridLines = false)
            }
            short -> {
                val lw = min(150 * d, w * 0.42f)
                label(c, title, r.left + p, r.top + 24 * d)
                big(c, value, unit, r.left + p, r.top + 68 * d, 34 * d)
                val rows = (((h - 104 * d) / (19 * d)).toInt() + 1).coerceIn(0, 4)
                spec.info.take(rows).forEachIndexed { i, it -> infoRow(c, it, r.left + p, r.left + lw - 8 * d, r.top + (94 + i * 19) * d) }
                g.set(r.left + lw, r.top + 14 * d, r.right - p, r.bottom - 14 * d)
                series(c, t.id, spec, plots, flush = false, gridLines = true)
            }
            small -> {
                label(c, title, r.left + p, r.top + 24 * d)
                big(c, value, unit, r.left + p, r.top + 68 * d, 34 * d)
                val rows = (((h * 0.55f - 106 * d) / (19 * d)).toInt() + 1).coerceIn(3, 4)
                spec.info.take(rows).forEachIndexed { i, it -> infoRow(c, it, r.left + p, r.right - p, r.top + (96 + i * 19) * d) }
                g.set(r.left, r.top + h * 0.55f, r.right, r.bottom)
                series(c, t.id, spec, plots, flush = true, gridLines = false)
            }
            else -> {
                label(c, title, r.left + p + d, r.top + 26 * d)
                spec.detail?.let { detail(c, it, r.right - p, r.top + 26 * d) }
                val vw = big(c, value, unit, r.left + p, r.top + 76 * d, 46 * d)
                var x = r.left + p + max(vw, 96 * d) + 24 * d
                val colW = if (w > 420 * d) 86 * d else 74 * d
                for (it in spec.info) {
                    val vt = infoText(it)
                    text.typeface = medium; text.textSize = 15 * d
                    val cw = max(colW, text.measureText(vt) + 16 * d)
                    if (x + cw - 16 * d > r.right - p) break
                    text.typeface = regular; text.textSize = 11 * d; text.color = sub; text.textAlign = Paint.Align.LEFT
                    c.drawText(it.name, x, r.top + 56 * d, text)
                    text.typeface = medium; text.textSize = 15 * d; text.color = ink
                    c.drawText(vt, x, r.top + 76 * d, text)
                    x += cw
                }
                val lg = if (spec.series.size > 1) 20 * d else 0f
                g.set(r.left + p, r.top + 94 * d, r.right - p, r.bottom - p - lg)
                series(c, t.id, spec, plots, flush = false, gridLines = true)
                if (lg > 0) legend(c, spec, r.left + p + 4 * d, r.bottom - p)
            }
        }
    }

    private fun graphFull(c: Canvas, t: DeckTile, spec: LiveSpec, plots: List<Plot>, r: RectF, title: String, value: String, unit: String) {
        val w = r.width(); val h = r.height()
        val k = (h / (183 * d)).coerceIn(0.62f, 1.5f)
        val p = 14 * d * k
        val header = min(h * .25f, 40 * d * k)
        val valueSize = min(header * .80f, 32 * d * k)
        label(c, title, r.left + p, r.top + header, max(9 * d, 11 * d * k))
        text.typeface = medium
        text.textSize = valueSize
        val valueWidth = text.measureText(value) + 22 * d * k
        big(c, value, unit, r.right - p - valueWidth, r.top + header, valueSize)
        val items = spec.info.take(4)
        val avail = w - 2 * p
        var perRow = max(1, items.size)
        var rows = if (items.isEmpty()) 0 else 1
        if (items.isNotEmpty() && avail / perRow < 74 * d * min(k, 1f)) { perRow = (items.size + 1) / 2; rows = 2 }
        val rowH = 38 * d * k
        if (rows > 0 && header + rows * rowH + 40 * d * k > h) rows = ((h - header - 40 * d * k) / rowH).toInt().coerceIn(0, rows)
        val column = avail / perRow
        items.take(rows * perRow).forEachIndexed { i, item ->
            val x = r.left + p + column * (i % perRow)
            val y0 = r.top + header + rowH * (i / perRow)
            text.textAlign = Paint.Align.LEFT
            text.typeface = regular
            text.textSize = 10 * d * k
            text.color = sub
            c.drawText(ellipsize(item.name, column - 5 * d * k), x, y0 + 14 * d * k, text)
            text.typeface = medium
            text.textSize = min(16 * d * k, column * .24f)
            text.color = ink
            c.drawText(ellipsize(infoText(item), column - 5 * d * k), x, y0 + 32 * d * k, text)
        }
        val graphTop = r.top + header + if (rows > 0) rows * rowH else 8 * d * k
        if (r.bottom > graphTop) {
            g.set(r.left, graphTop, r.right, r.bottom)
            series(c, t.id, spec, plots, flush = true, gridLines = true, k = k)
        }
    }

    private fun graphTall(c: Canvas, t: DeckTile, spec: LiveSpec, plots: List<Plot>, r: RectF, title: String, value: String, unit: String) {
        val w = r.width(); val h = r.height()
        val k = min(w / (131 * d), h / (183 * d)).coerceIn(0.62f, 1.5f)
        val p = 14 * d * k
        label(c, title, r.left + p, r.top + p + 11 * d * k, max(9 * d, 11 * d * k))
        big(c, value, unit, r.left + p, r.top + p + 47 * d * k, 32 * d * k)
        val gh = h * .38f
        val start = r.top + p + 67 * d * k
        val rowH = 17 * d * k
        val rows = (floor((r.bottom - gh - start) / rowH).toInt() + 1).coerceIn(0, spec.info.size)
        spec.info.take(rows).forEachIndexed { i, item ->
            val y = start + i * rowH
            text.typeface = medium; text.textSize = 12 * d * k; text.color = ink; text.textAlign = Paint.Align.RIGHT
            val vt = infoText(item)
            c.drawText(vt, r.right - p, y, text)
            val vw = text.measureText(vt)
            text.typeface = regular; text.textSize = 10.5f * d * k; text.color = sub; text.textAlign = Paint.Align.LEFT
            c.drawText(ellipsize(item.name, w - 2 * p - vw - 6 * d * k), r.left + p, y, text)
        }
        g.set(r.left, r.bottom - gh, r.right, r.bottom)
        series(c, t.id, spec, plots, flush = true, gridLines = false, k = k)
    }

    private fun graphStrip(c: Canvas, t: DeckTile, spec: LiveSpec, plots: List<Plot>, r: RectF, title: String, value: String, unit: String) {
        val h = r.height()
        val k = (h / (120 * d)).coerceIn(0.62f, 1.5f)
        val p = 14 * d * k
        val header = 40 * d * k
        val valueSize = 32 * d * k
        val labelSize = max(9 * d, 11 * d * k)
        label(c, title, r.left + p, r.top + header, labelSize)
        text.typeface = medium
        text.textSize = valueSize
        val vx = r.right - p - (text.measureText(value) + 22 * d * k)
        big(c, value, unit, vx, r.top + header, valueSize)
        text.typeface = bold; text.textSize = labelSize; text.letterSpacing = 0.12f
        val lw = text.measureText(title.uppercase(TR))
        text.letterSpacing = 0f
        val ix = r.left + p + lw + 16 * d * k
        val iw = vx - 16 * d * k - ix
        if (iw > 120 * d * k && spec.info.isNotEmpty()) {
            text.typeface = regular; text.textSize = 11.5f * d * k; text.color = sub; text.textAlign = Paint.Align.LEFT
            c.drawText(ellipsize(spec.info.take(3).joinToString("  ·  ") { infoText(it) }, iw), ix, r.top + header, text)
        }
        val top = r.top + header + 8 * d * k
        if (r.bottom > top) {
            g.set(r.left, top, r.right, r.bottom)
            series(c, t.id, spec, plots, flush = true, gridLines = true, k = k)
        }
    }

    private fun ring(c: Canvas, t: DeckTile, spec: LiveSpec, r: RectF) {
        val s = spec.series.firstOrNull() ?: return
        val p = 14 * d
        val v = cur(s.key)
        val mx = if (s.max > 0) s.max else 100f
        val f = ((v - s.min) / (mx - s.min)).coerceIn(0f, 1f)
        label(c, t.label ?: s.name, r.left + p, r.top + 24 * d)
        if (s.unit != "%") detail(c, "${(f * 100).toInt()}%", r.right - p, r.top + 24 * d)
        val rad = min(r.width(), r.height()) * 0.3f
        val cx = r.centerX(); val cy = r.centerY() + 12 * d
        oval.set(cx - rad, cy - rad, cx + rad, cy + rad)
        stroke.pathEffect = null; stroke.strokeCap = Paint.Cap.ROUND; stroke.strokeWidth = max(6 * d, rad * 0.2f)
        stroke.color = track
        c.drawArc(oval, 135f, 270f, false, stroke)
        stroke.color = colorFor(spec, s, f)
        if (f > 0.005f) c.drawArc(oval, 135f, 270f * f, false, stroke)
        val vs = (rad * 0.5f).coerceIn(16 * d, 48 * d)
        text.textAlign = Paint.Align.CENTER
        text.typeface = medium; text.textSize = vs; text.color = ink
        if (s.unit == "%") {
            c.drawText("${fmt(v, 0)}%", cx, cy + vs * 0.35f, text)
        } else {
            c.drawText(valueText(s), cx, cy + vs * 0.2f, text)
            text.typeface = regular; text.textSize = vs * 0.5f; text.color = sub
            c.drawText("/ ${fmt(mx, 0)} ${s.unit}", cx, cy + vs * 0.2f + vs * 0.75f, text)
        }
        text.textAlign = Paint.Align.LEFT
    }

    private fun gauge(c: Canvas, t: DeckTile, spec: LiveSpec, r: RectF) {
        val s = spec.series.firstOrNull() ?: return
        val p = 14 * d
        val lo = s.min
        val hi = if (s.max > 0) s.max else 100f
        val v = cur(s.key)
        val f = ((v - lo) / (hi - lo)).coerceIn(0f, 1f)
        label(c, t.label ?: s.name, r.left + p, r.top + 24 * d)
        val rad = min(r.width() * 0.36f, (r.height() - 58 * d) * 0.95f).coerceAtLeast(20 * d)
        val cx = r.centerX(); val cy = r.bottom - max(34 * d, rad * 0.6f)
        oval.set(cx - rad, cy - rad, cx + rad, cy + rad)
        stroke.pathEffect = null; stroke.strokeCap = Paint.Cap.BUTT; stroke.strokeWidth = max(6 * d, rad * 0.14f)
        stroke.color = track
        c.drawArc(oval, 180f, 180f, false, stroke)
        val segs = 40
        val load = spec.colorMode == "load"
        val base = s.color
        var i = 0
        while (i < segs && i.toFloat() / segs < f) {
            val t0 = i.toFloat() / segs
            val t1 = min((i + 1f) / segs, f)
            stroke.color = if (load) loadColor(t0) else withAlpha(base, 0.45f + 0.55f * t0)
            c.drawArc(oval, 180f + 180f * t0, 180f * (t1 - t0) + 0.6f, false, stroke)
            i++
        }
        stroke.strokeWidth = max(1f, d); stroke.color = withAlpha(sub, 0.35f)
        val steps = 6
        for (k in 0..steps) {
            val a = Math.PI + Math.PI * k / steps
            val r1 = rad + stroke.strokeWidth + 6 * d; val r2 = r1 + 4 * d
            c.drawLine(cx + cos(a).toFloat() * r1, cy + sin(a).toFloat() * r1, cx + cos(a).toFloat() * r2, cy + sin(a).toFloat() * r2, stroke)
        }
        val vs = (rad * 0.46f).coerceIn(16 * d, 44 * d)
        text.textAlign = Paint.Align.CENTER
        text.typeface = medium; text.textSize = vs; text.color = ink
        val unit = if (s.unit == "°C") "°" else s.unit
        c.drawText(fmt(v, s.dec) + unit, cx + if (unit == "°") 3 * d else 0f, cy - 2 * d, text)
        text.typeface = regular; text.textSize = 10 * d; text.color = sub
        c.drawText(fmt(lo, 0), cx - rad, cy + 16 * d, text)
        c.drawText(fmt(hi, 0), cx + rad, cy + 16 * d, text)
        text.textAlign = Paint.Align.LEFT
    }

    private fun net(c: Canvas, t: DeckTile, spec: LiveSpec, r: RectF) {
        val down = spec.series.getOrNull(0) ?: return
        val up = spec.series.getOrNull(1) ?: down
        val plots = listOf(Plot(down, mirror = true), Plot(up, mirror = true, down = true))
        val p = 14 * d
        label(c, t.label ?: I18n.t("Network"), r.left + p, r.top + 24 * d)
        if (r.height() < 200 * d) {
            val lw = min(140 * d, r.width() * 0.42f)
            text.typeface = medium; text.textSize = 20 * d
            text.color = down.color; c.drawText("↓ " + fmt(cur(down.key), down.dec), r.left + p, r.top + 62 * d, text)
            text.color = up.color; c.drawText("↑ " + fmt(cur(up.key), up.dec), r.left + p, r.top + 92 * d, text)
            text.typeface = regular; text.textSize = 11 * d; text.color = sub
            c.drawText(down.unit + (spec.detail?.let { " · $it" } ?: ""), r.left + p, r.top + 116 * d, text)
            g.set(r.left + lw, r.top + 14 * d, r.right - p, r.bottom - 14 * d)
            series(c, t.id, spec, plots, flush = false, gridLines = true, dots = false)
            return
        }
        spec.detail?.let { detail(c, it, r.right - p, r.top + 24 * d) }
        val half = r.left + r.width() / 2 + 4 * d
        text.typeface = regular; text.textSize = 11 * d; text.color = sub
        c.drawText(down.name.uppercase(TR), r.left + p, r.top + 50 * d, text)
        c.drawText(up.name.uppercase(TR), half, r.top + 50 * d, text)
        val w1 = big(c, "↓ " + fmt(cur(down.key), down.dec), "", r.left + p, r.top + 78 * d, 26 * d, down.color)
        unitAfter(c, down.unit, r.left + p + w1 + 5 * d, r.top + 78 * d)
        val w2 = big(c, "↑ " + fmt(cur(up.key), up.dec), "", half, r.top + 78 * d, 26 * d, up.color)
        unitAfter(c, up.unit, half + w2 + 5 * d, r.top + 78 * d)
        g.set(r.left + p, r.top + 94 * d, r.right - p, r.bottom - p)
        series(c, t.id, spec, plots, flush = false, gridLines = true, dots = false)
        stroke.pathEffect = null; stroke.strokeWidth = max(1f, d); stroke.color = withAlpha(sub, 0.25f)
        c.drawLine(g.left, g.centerY(), g.right, g.centerY(), stroke)
        text.typeface = regular; text.textSize = 10 * d; text.color = sub; text.textAlign = Paint.Align.RIGHT
        c.drawText("${fmt(autoMax["${t.id}|${down.key}"] ?: 0f, 0)} ${down.unit}", g.right - 4 * d, g.top + 12 * d, text)
        c.drawText("${fmt(autoMax["${t.id}|${up.key}"] ?: 0f, 0)} ${up.unit}", g.right - 4 * d, g.bottom - 5 * d, text)
        text.textAlign = Paint.Align.LEFT
    }

    private fun procs(c: Canvas, t: DeckTile, r: RectF) {
        val p = 14 * d
        val color = t.color ?: Color.parseColor("#FF3D5A")
        label(c, t.label ?: I18n.t("Top CPU"), r.left + p, r.top + 24 * d)
        detail(c, "son 1 sn", r.right - p, r.top + 24 * d)
        val rows = ((r.height() - 40 * d) / (28 * d)).toInt().coerceIn(1, 5)
        val list = LiveStore.procs.take(rows)
        val top = list.maxOfOrNull { it.second } ?: 0f
        val scale = max(30f, top * 1.1f)
        val nameW = min(110 * d, r.width() * 0.3f)
        list.forEachIndexed { i, (name, v) ->
            val key = "${t.id}|$name"
            val dv = (smooth[key] ?: v).let { it + (v - it) * 0.15f }
            smooth[key] = dv
            val y = r.top + (50 + i * 28) * d
            text.typeface = regular; text.textSize = 13 * d; text.color = ink; text.textAlign = Paint.Align.LEFT
            c.drawText(ellipsize(name, nameW - 8 * d), r.left + p, y + 4 * d, text)
            val bx = r.left + p + nameW
            val bw = r.right - p - 46 * d - bx
            fill.shader = null; fill.color = track
            oval.set(bx, y - 4 * d, bx + bw, y + 4 * d)
            c.drawRoundRect(oval, 4 * d, 4 * d, fill)
            fill.color = if (t.live?.colorMode == "load") loadColor(dv / 100f) else color
            oval.set(bx, y - 4 * d, bx + max(4 * d, bw * (dv / scale).coerceIn(0f, 1f)), y + 4 * d)
            c.drawRoundRect(oval, 4 * d, 4 * d, fill)
            text.typeface = medium; text.textAlign = Paint.Align.RIGHT
            c.drawText(fmt(dv, 1) + "%", r.right - p, y + 4 * d, text)
            text.textAlign = Paint.Align.LEFT
        }
        if (list.isEmpty()) {
            text.typeface = regular; text.textSize = 12 * d; text.color = sub
            c.drawText(I18n.t("Measuring…"), r.left + p, r.top + 54 * d, text)
        }
    }

    private fun series(c: Canvas, id: String, spec: LiveSpec, plots: List<Plot>, flush: Boolean, gridLines: Boolean, dots: Boolean = true, k: Float = 1f) {
        val n = spec.seconds + 1
        val phase = LiveStore.phase()
        val step = g.width() / (n - 2)
        fun xAt(i: Int) = g.right - (n - 1 - i) * step + (1 - phase) * step

        c.save()
        c.clipRect(g)
        if (gridLines) {
            stroke.pathEffect = null; stroke.strokeWidth = max(1f, d); stroke.color = grid
            for (f in floatArrayOf(0.25f, 0.5f, 0.75f)) {
                val y = g.top + g.height() * f
                c.drawLine(g.left, y, g.right, y, stroke)
            }
            val every = if (n > 100) 60 else 10
            for (i in 0 until n) if ((LiveStore.tick - (n - 1 - i)) % every == 0L) {
                val x = xAt(i)
                c.drawLine(x, g.top, x, g.bottom, stroke)
            }
        }
        val usable = g.height() * if (flush) 0.82f else 0.94f
        val order = if (plots.size > 1 && !plots[0].mirror) plots.drop(1) + plots[0] else plots
        for (pl in order) {
            val s = pl.s
            val mx = if (s.max > 0) s.max else scaleFor(id, s, n)
            val base = if (pl.mirror) g.centerY() else g.bottom
            val hgt = if (pl.mirror) g.height() / 2 * 0.9f else usable
            val dir = if (pl.down) 1f else -1f
            fun yOf(v: Float) = base + dir * ((v - s.min) / (mx - s.min)).coerceIn(0f, 1f) * hgt
            val curV = LiveStore.get(s.key, 1) + (LiveStore.get(s.key, 0) - LiveStore.get(s.key, 1)) * phase
            val col = colorFor(spec, s, curV / mx)
            val style = if (s.dash) "line" else spec.style

            if (style == "bars") {
                val bw = max(1f, step * 0.62f)
                for (i in 0 until n) {
                    val v = LiveStore.get(s.key, n - 1 - i)
                    val y = yOf(v)
                    val bc = if (spec.colorMode == "load" && s.loadable) loadColor(v / mx) else col
                    fill.shader = null
                    fill.color = withAlpha(bc, if (i == n - 1) 0.95f else 0.55f + 0.35f * i / n)
                    val x = xAt(i) - bw / 2
                    oval.set(x, min(y, base), x + bw, max(y, base).let { if (it - min(y, base) < 1.5f * d) min(y, base) + 1.5f * d else it })
                    c.drawRoundRect(oval, min(2 * d, bw / 2), min(2 * d, bw / 2), fill)
                }
                continue
            }

            line.reset()
            for (i in 0 until n) {
                val x = xAt(i); val y = yOf(LiveStore.get(s.key, n - 1 - i))
                if (i == 0) line.moveTo(x, y) else line.lineTo(x, y)
            }
            if (style == "area" && !s.dash) {
                area.set(line)
                area.lineTo(xAt(n - 1), base)
                area.lineTo(xAt(0), base)
                area.close()
                fill.shader = gradient(col, base + dir * hgt, base)
                c.drawPath(area, fill)
                fill.shader = null
            }
            stroke.strokeCap = Paint.Cap.ROUND
            stroke.strokeWidth = (if (s.dash) 1.5f * d else 2f * d) * max(0.8f, k)
            stroke.color = if (s.dash) withAlpha(col, 0.85f) else col
            stroke.pathEffect = if (s.dash) dash else null
            c.drawPath(line, stroke)
            stroke.pathEffect = null

            if (dots && !s.dash) {
                val ex = g.right - (if (flush) 7 * d else 4 * d) * k
                val ey = yOf(curV)
                fill.shader = null
                fill.color = withAlpha(col, 0.25f); c.drawCircle(ex, ey, 7 * d * k, fill)
                fill.color = col; c.drawCircle(ex, ey, 3.5f * d * k, fill)
                fill.color = bg; c.drawCircle(ex, ey, 1.4f * d * k, fill)
            }
        }
        c.restore()
    }

    private fun scaleFor(id: String, s: Series, n: Int): Float {
        var m = 0f
        for (i in 0 until n) m = max(m, LiveStore.get(s.key, i))
        val target = max(1f, m * 1.15f)
        val key = "$id|${s.key}"
        val now = autoMax[key]?.let { it + (target - it) * 0.08f } ?: target
        autoMax[key] = now
        return now
    }

    private fun gradient(col: Int, from: Float, to: Float): Shader {
        val key = "$col|${from.toInt()}|${to.toInt()}"
        shaders[key]?.let { return it }
        if (shaders.size > 96) shaders.clear()
        return LinearGradient(0f, from, 0f, to, withAlpha(col, 0.42f), withAlpha(col, 0.02f), Shader.TileMode.CLAMP)
            .also { shaders[key] = it }
    }

    private fun legend(c: Canvas, spec: LiveSpec, x0: Float, baseline: Float) {
        var x = x0
        text.typeface = regular; text.textSize = 11 * d; text.textAlign = Paint.Align.LEFT
        for (s in spec.series) {
            stroke.color = s.color; stroke.strokeWidth = 2 * d; stroke.strokeCap = Paint.Cap.BUTT
            stroke.pathEffect = if (s.dash) DashPathEffect(floatArrayOf(3 * d, 3 * d), 0f) else null
            c.drawLine(x, baseline - 4 * d, x + 14 * d, baseline - 4 * d, stroke)
            stroke.pathEffect = null
            text.color = sub
            c.drawText(s.name, x + 19 * d, baseline, text)
            x += 24 * d + text.measureText(s.name) + 12 * d
        }
    }

    private fun label(c: Canvas, s: String, x: Float, y: Float, size: Float = 11 * d) {
        text.typeface = bold; text.textSize = size; text.color = sub; text.textAlign = Paint.Align.LEFT
        text.letterSpacing = 0.12f
        c.drawText(s.uppercase(TR), x, y, text)
        text.letterSpacing = 0f
    }

    private fun detail(c: Canvas, s: String, x: Float, y: Float) {
        text.typeface = regular; text.textSize = 12 * d; text.color = sub; text.textAlign = Paint.Align.RIGHT
        c.drawText(s, x, y, text)
        text.textAlign = Paint.Align.LEFT
    }

    private fun big(c: Canvas, value: String, unit: String, x: Float, y: Float, size: Float, color: Int = ink): Float {
        text.typeface = medium; text.textSize = size; text.color = color; text.textAlign = Paint.Align.LEFT
        c.drawText(value, x, y, text)
        val w = text.measureText(value)
        if (unit.isEmpty()) return w
        text.textSize = Math.round(size * 0.42f).toFloat(); text.color = sub
        c.drawText(unit, x + w + 3 * d, y, text)
        return w + 3 * d + text.measureText(unit)
    }

    private fun unitAfter(c: Canvas, unit: String, x: Float, y: Float) {
        text.typeface = regular; text.textSize = 12 * d; text.color = sub
        c.drawText(unit, x, y, text)
    }

    private fun infoRow(c: Canvas, it: InfoItem, x: Float, right: Float, y: Float) {
        text.typeface = regular; text.textSize = 12 * d; text.color = sub; text.textAlign = Paint.Align.LEFT
        c.drawText(it.name, x, y, text)
        text.typeface = medium; text.color = ink; text.textAlign = Paint.Align.RIGHT
        c.drawText(infoText(it), right, y, text)
        text.textAlign = Paint.Align.LEFT
    }

    private fun ellipsize(s: String, maxW: Float): String {
        if (text.measureText(s) <= maxW) return s
        var e = s
        while (e.length > 1 && text.measureText("$e…") > maxW) e = e.dropLast(1)
        return "$e…"
    }

    private fun cur(key: String) = LiveStore.latest[key] ?: 0f

    private fun fmt(v: Float, dec: Int): String = if (dec <= 0) Math.round(v).toString() else String.format(TR, "%.${dec}f", v)

    private fun valueText(s: Series): String = if (s.unit == "süre") duration(cur(s.key)) else fmt(cur(s.key), s.dec)
    private fun unitText(s: Series): String = if (s.unit == "süre") "" else s.unit

    private fun infoText(i: InfoItem): String {
        val v = cur(i.key)
        return when {
            i.unit == "süre" -> duration(v)
            i.unit == "GB" && i.max > 0 -> "${fmt(v, i.dec)} / ${fmt(i.max, 0)} GB"
            i.unit.isEmpty() -> fmt(v, i.dec)
            i.unit == "%" || i.unit == "°C" -> fmt(v, i.dec) + (if (i.unit == "%") "%" else " °C")
            else -> "${fmt(v, i.dec)} ${i.unit}"
        }
    }

    private fun duration(sec: Float): String {
        val m = (sec / 60).toLong()
        val days = m / 1440; val hours = (m / 60) % 24; val mins = m % 60
        return when {
            days > 0 -> "${days}g ${hours}s"
            hours > 0 -> "${hours}s ${mins}d"
            else -> "${mins}d"
        }
    }

    private fun colorFor(spec: LiveSpec, s: Series, f: Float): Int =
        if (spec.colorMode == "load" && s.loadable) loadColor(f) else s.color

    private fun loadColor(f0: Float): Int {
        val f = Math.round(f0.coerceIn(0f, 1f) * 32) / 32f
        return when {
            f <= 0.55f -> lerpColor(Color.rgb(74, 222, 128), Color.rgb(255, 176, 0), f / 0.55f)
            f <= 0.85f -> lerpColor(Color.rgb(255, 176, 0), Color.rgb(255, 61, 90), (f - 0.55f) / 0.30f)
            else -> Color.rgb(255, 61, 90)
        }
    }

    private fun lerpColor(a: Int, b: Int, t: Float): Int = Color.rgb(
        (Color.red(a) + (Color.red(b) - Color.red(a)) * t).toInt(),
        (Color.green(a) + (Color.green(b) - Color.green(a)) * t).toInt(),
        (Color.blue(a) + (Color.blue(b) - Color.blue(a)) * t).toInt(),
    )

    private fun withAlpha(c: Int, a: Float): Int = Color.argb((a.coerceIn(0f, 1f) * 255).toInt(), Color.red(c), Color.green(c), Color.blue(c))
}
