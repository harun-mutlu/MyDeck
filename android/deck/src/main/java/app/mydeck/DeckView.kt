package app.mydeck

import android.animation.ValueAnimator
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapShader
import android.graphics.Matrix
import android.graphics.PorterDuff
import android.graphics.PorterDuffXfermode
import android.graphics.Region
import android.graphics.Shader
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Rect
import android.graphics.RectF
import android.graphics.Typeface
import android.os.SystemClock
import android.text.TextPaint
import android.text.TextUtils
import android.view.MotionEvent
import android.view.View
import android.view.animation.DecelerateInterpolator
import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min

class DeckView(context: Context) : View(context) {
    interface Listener {
        fun onPress(tile: DeckTile, gesture: String)

        fun onPageShown(pageId: String) {}
        fun onSlide(tile: DeckTile, value: Float)

        fun onAnyTouch() {}

        fun onBrightness(value: Float) {}

        fun onSlideMute(tile: DeckTile) {}

        fun onMedia(tile: DeckTile, cmd: String, value: Float) {}
    }

    var listener: Listener? = null

    private var layout: Layout? = null
    private var pageId: String? = null
    private val folderStack = ArrayList<String>()

    private val density = resources.displayMetrics.density
    private val layers = HashMap<String, Bitmap>()

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG)
    private val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val bmpPaint = Paint(Paint.FILTER_BITMAP_FLAG)
    private val labelPaint = TextPaint(Paint.ANTI_ALIAS_FLAG).apply {
        typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)
    }
    private val subPaint = TextPaint(Paint.ANTI_ALIAS_FLAG).apply { typeface = Typeface.DEFAULT }
    private val rect = RectF()
    private val src = Rect()
    private val live = LiveRenderer(density)
    private val media = MediaRenderer(density)
    private var mediaDown: DeckTile? = null
    private val widgets = WidgetRenderer(density, context)
    private var timerDown: DeckTile? = null
    private val pulseRect = RectF()

    var wallBitmap: Bitmap? = null
        set(v) { field = v; invalidate() }
    var videoWall = false
        set(v) { field = v; invalidate() }

    var onWallChanged: ((Wall?) -> Unit)? = null
    private var shownWall: String? = "?"
    private val dimPaint = Paint()
    private val clearPaint = Paint().apply { xfermode = PorterDuffXfermode(PorterDuff.Mode.CLEAR) }
    private val gifPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val gifMatrix = Matrix()
    private val gifPressAt = HashMap<String, Long>()
    private val gifOnceAt = HashMap<String, Long>()
    private var mediaHit: Pair<String, Float>? = null

    init {
        MediaStore.onChange = { invalidate() }
        GifStore.onReady = { invalidate() }
        TimerStore.onChange = { invalidate() }
        WeatherStore.onChange = { invalidate() }
        PluginStore.onChange = { invalidate() }
    }

    private class Press(val tile: DeckTile, val pageId: String, var downAt: Long, var upAt: Long = -1)
    private val presses = ArrayList<Press>()

    private var downX = 0f
    private var downY = 0f
    private var downTile: DeckTile? = null
    private var swiping = false
    private var dragX = 0f
    private var slideAnim: ValueAnimator? = null
    private var fadeFrom: Bitmap? = null
    private var fadeStart = 0L

    private val toggles = HashMap<String, Boolean>()
    private val values = HashMap<String, Float>()
    private val lastLocal = HashMap<String, Long>()
    private var sliderDrag: DeckTile? = null
    private val mutes = HashMap<String, Boolean>()
    private val muteLocal = HashMap<String, Long>()
    private var muteDown: DeckTile? = null
    private val muteCenters = HashMap<String, android.graphics.PointF>()
    private val iconPath = android.graphics.Path()
    private val pendingValues = HashMap<String, Float>()
    private val applyPending: Runnable = Runnable {
        val now = SystemClock.uptimeMillis()
        var waiting = false
        val it = pendingValues.entries.iterator()
        while (it.hasNext()) {
            val (id, v) = it.next()
            if (sliderDrag?.id == id || now - (lastLocal[id] ?: 0L) < 700) { waiting = true; continue }
            values[id] = v
            it.remove()
        }
        if (waiting) postDelayed(this.applyPendingRef(), 300)
        invalidate()
    }
    private fun applyPendingRef(): Runnable = applyPending
    private var lastSlideSent = 0L

    private var toast: String? = null
    private var toastUntil = 0L

    private val broken = HashSet<String>()

    private fun drawBroken(c: Canvas, t: DeckTile, page: DeckPage, th: Theme, dx: Float) {
        tileRect(t, page, th, rect)
        rect.offset(dx, 0f)
        val r = 10f * density
        fill.color = Color.argb(150, 40, 16, 22)
        c.drawRoundRect(rect, r, r, fill)
        stroke.color = Color.argb(200, 255, 61, 90); stroke.strokeWidth = 1.5f * density
        c.drawRoundRect(rect, r, r, stroke)
        val s = min(rect.width(), rect.height()) * 0.18f
        fill.color = Color.argb(220, 255, 61, 90)
        c.drawRect(rect.centerX() - s * 0.09f, rect.centerY() - s * 0.6f, rect.centerX() + s * 0.09f, rect.centerY() + s * 0.2f, fill)
        c.drawCircle(rect.centerX(), rect.centerY() + s * 0.5f, s * 0.11f, fill)
    }

    fun setLayout(l: Layout) {
        val keep = pageId
        broken.clear()
        removeCallbacks(holdRunnable); removeCallbacks(singleRunnable); holdTile = null; tapTile = null
        layout = l
        recycleLayers()
        folderStack.clear()
        pageId = if (keep != null && l.page(keep)?.folder == false) keep else l.start
        if (l.page(pageId) == null) pageId = l.pages.firstOrNull()?.id
        presses.clear()
        l.pages.forEach { p -> p.tiles.filter { it.sm == "bri" }.forEach { values[it.id] = brightness } }
        TimerStore.sync(l)
        GifStore.retain(l.pages.flatMap { pg -> pg.tiles.mapNotNull { it.gif?.url } }.toSet())
        notifyWall()
        invalidate()
    }

    private fun wallFor(page: DeckPage?): Wall? = page?.wall ?: layout?.theme?.wall

    private fun cardAlpha(page: DeckPage, th: Theme): Int =
        if (wallFor(page) != null) ((1f - th.glass) * 255).toInt().coerceIn(40, 255) else 255

    private var reportedPage: String? = null

    private fun notifyWall() {
        val w = wallFor(layout?.page(pageId))
        if (w?.name != shownWall) { shownWall = w?.name; onWallChanged?.invoke(w) }
        val id = pageId
        if (id != null && id != reportedPage) { reportedPage = id; listener?.onPageShown(id) }
    }

    fun showPage(id: String) {
        val target = layout?.page(id) ?: return
        if (target.id == pageId) return
        cancelGesture()
        goTo(target.id, pushFolder = target.folder)
    }

    fun goBack() { cancelGesture(); back() }

    private val holdMs = 450L
    private val doubleMs = 280L
    private var holdTile: DeckTile? = null
    private var held = false
    private var tapTile: DeckTile? = null
    private val holdRunnable = Runnable {
        val t = holdTile ?: return@Runnable
        if (downTile !== t) return@Runnable
        held = true
        holdTile = null
        performHapticFeedback(android.view.HapticFeedbackConstants.LONG_PRESS)
        presses.lastOrNull { it.tile === t && it.upAt < 0 }?.upAt = SystemClock.uptimeMillis()
        listener?.onPress(t, "hold")
        invalidate()
    }
    private val singleRunnable = Runnable {
        val t = tapTile ?: return@Runnable
        tapTile = null
        activate(t)
    }

    private fun cancelGesture() {
        removeCallbacks(holdRunnable)
        holdTile = null
    }

    private fun release(t: DeckTile) {
        if (!t.dbl) { activate(t); return }
        if (tapTile === t) {
            removeCallbacks(singleRunnable)
            tapTile = null
            gifPressAt.remove(t.id)
            listener?.onPress(t, "double")
            return
        }
        tapTile?.let { removeCallbacks(singleRunnable); activate(it) }
        tapTile = t
        postDelayed(singleRunnable, doubleMs)
    }

    private fun drawBackground(c: Canvas, page: DeckPage, th: Theme) {
        val w = wallFor(page)
        if (w == null) { c.drawColor(th.bg); return }
        val b = wallBitmap
        when {
            w.type == "image" && b != null && !b.isRecycled -> {
                rect.set(0f, 0f, width.toFloat(), height.toFloat())
                c.drawBitmap(b, null, rect, bmpPaint)
            }
            w.type == "video" && videoWall -> {}
            else -> c.drawColor(th.bg)
        }
        dimPaint.color = Color.argb((th.dim * 255).toInt().coerceIn(0, 230), 4, 5, 9)
        c.drawRect(0f, 0f, width.toFloat(), height.toFloat(), dimPaint)
    }

    fun showToast(text: String) {
        toast = text
        toastUntil = SystemClock.uptimeMillis() + 2600
        invalidate()
    }

    val hasLayout: Boolean get() = layout != null

    fun applyStates(states: org.json.JSONObject) {
        val now = SystemClock.uptimeMillis()
        val keys = states.keys()
        while (keys.hasNext()) {
            val id = keys.next()
            val st = states.optJSONObject(id) ?: continue
            if (st.has("on")) {
                val on = st.optBoolean("on")
                if (toggles[id] != on) { toggles[id] = on; redrawInLayers(id) }
            }
            if (st.has("m") && now - (muteLocal[id] ?: 0L) > 700) mutes[id] = st.optBoolean("m")
            if (st.has("v")) {
                val v = st.optDouble("v", -1.0).toFloat()
                if (sliderDrag?.id == id || now - (lastLocal[id] ?: 0L) < 700) {
                    pendingValues[id] = v
                    removeCallbacks(applyPending)
                    postDelayed(applyPending, 750)
                    continue
                }
                pendingValues.remove(id)
                values[id] = v
            }
        }
        invalidate()
    }

    private fun redrawInLayers(tileId: String) {
        val l = layout ?: return
        for (page in l.pages) {
            val layer = layers[page.id] ?: continue
            val t = page.tiles.firstOrNull { it.id == tileId } ?: continue
            if (t.gif != null) continue
            val c = Canvas(layer)
            tileRect(t, page, l.theme, rect)
            c.drawRect(rect.left - 2, rect.top - 2, rect.right + 2, rect.bottom + 2, clearPaint)
            drawTile(c, t, page, l.theme)
        }
    }

    private fun cell(page: DeckPage, th: Theme): FloatArray {
        val cols = maxOf(1, page.cols)
        val rows = maxOf(1, page.rows)
        val m = th.pad
        val g = th.gap
        val aw = width - 2 * m
        val ah = height - 2 * m
        val cw = (aw - g * (cols - 1)) / cols
        val ch = (ah - g * (rows - 1)) / rows
        if (!page.square) return floatArrayOf(cw, ch, m, m, g, g)
        val s = maxOf(1f, minOf(cw, ch))
        val w: Float; val gx: Float; val h: Float; val gy: Float
        if (cols > 1) { w = s; gx = (aw - cols * s) / (cols - 1) } else { w = aw; gx = g }
        if (rows > 1) { h = s; gy = (ah - rows * s) / (rows - 1) } else { h = ah; gy = g }
        return floatArrayOf(w, h, m, m, gx, gy)
    }

    private fun tileRect(t: DeckTile, page: DeckPage, th: Theme, out: RectF) {
        val c = cell(page, th)
        val x = c[2] + t.x * (c[0] + c[4])
        val y = c[3] + t.y * (c[1] + c[5])
        out.set(x, y, x + c[0] * t.w + c[4] * (t.w - 1), y + c[1] * t.h + c[5] * (t.h - 1))
    }

    private fun tileAt(page: DeckPage, th: Theme, x: Float, y: Float): DeckTile? {
        val r = RectF()
        for (t in page.tiles) {
            tileRect(t, page, th, r)
            if (r.contains(x, y)) return t
        }
        return null
    }

    private fun recycleLayers() {
        for (b in layers.values) b.recycle()
        layers.clear()
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        recycleLayers()
    }

    private fun layerFor(page: DeckPage): Bitmap? {
        val l = layout ?: return null
        if (width <= 0 || height <= 0) return null
        layers[page.id]?.let { return it }

        if (layers.size >= 4) {
            val drop = layers.keys.firstOrNull { it != pageId } ?: layers.keys.first()
            layers.remove(drop)?.recycle()
        }
        val bmp = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        val c = Canvas(bmp)

        for (t in page.tiles) if (t.gif == null) drawTile(c, t, page, l.theme)
        drawPageDots(c, page, l)
        layers[page.id] = bmp
        return bmp
    }

    private fun mix(a: Int, b: Int, k: Float): Int {
        val t = k.coerceIn(0f, 1f)
        return Color.rgb(
            (Color.red(a) + (Color.red(b) - Color.red(a)) * t).toInt(),
            (Color.green(a) + (Color.green(b) - Color.green(a)) * t).toInt(),
            (Color.blue(a) + (Color.blue(b) - Color.blue(a)) * t).toInt(),
        )
    }

    private fun drawTile(c: Canvas, t: DeckTile, page: DeckPage, th: Theme, gifFrame: Bitmap? = null) {
        tileRect(t, page, th, rect)
        val isOn = t.kind == "toggle" && toggles[t.id] == true
        val alpha = cardAlpha(page, th)
        labelPaint.clearShadowLayer(); subPaint.clearShadowLayer()
        if (t.media != null) return
        if (t.widget) return
        if (t.live != null) {
            fill.color = t.bg ?: th.tile
            fill.alpha = alpha
            c.drawRoundRect(rect, th.radius, th.radius, fill)
            fill.alpha = 255
            return
        }
        val variant = if (isOn) t.on else null
        val accent = variant?.color ?: t.color ?: if (isOn) th.press else null
        val label = variant?.label ?: t.label
        val subText = variant?.sub ?: t.sub
        fill.color = t.bg ?: if (accent != null) mix(th.tile, accent, 0.14f) else th.tile
        fill.alpha = alpha
        c.drawRoundRect(rect, th.radius, th.radius, fill)
        fill.alpha = 255
        val gifBg = t.gif?.fill == "bg" && gifFrame != null && !gifFrame.isRecycled
        if (gifBg) {
            val sh = BitmapShader(gifFrame!!, Shader.TileMode.CLAMP, Shader.TileMode.CLAMP)
            gifMatrix.reset()
            gifMatrix.setScale(rect.width() / gifFrame.width, rect.height() / gifFrame.height)
            gifMatrix.postTranslate(rect.left, rect.top)
            sh.setLocalMatrix(gifMatrix)
            gifPaint.shader = sh
            c.drawRoundRect(rect, th.radius, th.radius, gifPaint)
            gifPaint.shader = null
            val shadow = Color.argb(170, 0, 0, 0)
            labelPaint.setShadowLayer(4f * density, 0f, 1f * density, shadow)
            subPaint.setShadowLayer(4f * density, 0f, 1f * density, shadow)
        }
        stroke.strokeWidth = max(1f, density)
        stroke.color = if (accent != null) mix(th.border, accent, 0.75f) else th.border
        if (alpha < 255) stroke.alpha = (90 + alpha * 0.6f).toInt().coerceAtMost(255)
        c.drawRoundRect(rect, th.radius, th.radius, stroke)
        stroke.alpha = 255

        val w = rect.width()
        val h = rect.height()
        val icon = if (t.gif?.fill == "icon" && gifFrame != null && !gifFrame.isRecycled) gifFrame else (variant?.icon ?: t.icon)
        val labelSize = (min(h * 0.13f, w * 0.12f)).coerceIn(10f * density, 17f * density)
        labelPaint.textSize = labelSize
        labelPaint.color = th.text
        subPaint.textSize = labelSize * 0.8f
        subPaint.color = if (accent != null) mix(th.sub, accent, 0.45f) else th.sub

        if (t.kind == "slider") {
            val g = sliderGeom(t, rect, labelSize)
            if (label != null) {
                if (g.vertical) drawText(c, label, labelPaint, rect.centerX(), g.labelBaseline, w - 12 * density, center = true)
                else drawText(c, label, labelPaint, g.track.left, g.labelBaseline, w * 0.6f, center = false)
            }
            return
        }

        val horizontal = t.w >= 2 && t.h == 1 && icon != null && label != null
        if (horizontal) {
            val ix = rect.left + h * 0.22f
            val iy = rect.centerY() - icon!!.height / 2f
            c.drawBitmap(icon, ix, iy, bmpPaint)
            val tx = ix + icon.width + h * 0.18f
            val maxW = rect.right - tx - h * 0.18f
            val lines = if (subText != null) 2 else 1
            val lineH = labelSize * 1.25f
            var ty = rect.centerY() - (lines * lineH) / 2f + labelSize
            drawText(c, label!!, labelPaint, tx, ty, maxW, center = false)
            if (subText != null) { ty += lineH; drawText(c, subText, subPaint, tx, ty, maxW, center = false) }
            return
        }

        val labelH = if (label != null) labelSize * 1.3f else 0f
        val subH = if (subText != null && h > 90 * density) labelSize * 1.1f else 0f
        val iconH = icon?.height?.toFloat() ?: 0f
        val gap = if (icon != null && labelH > 0) h * 0.08f else 0f
        val total = iconH + gap + labelH + subH
        var y = rect.centerY() - total / 2f
        if (icon != null) {
            c.drawBitmap(icon, rect.centerX() - icon.width / 2f, y, bmpPaint)
            y += iconH + gap
        }
        val maxW = w - 12 * density
        if (label != null) drawText(c, label, labelPaint, rect.centerX(), y + labelSize, maxW, center = true)
        if (subH > 0) drawText(c, subText!!, subPaint, rect.centerX(), y + labelH + subPaint.textSize, maxW, center = true)
    }

    private class SliderGeom(val vertical: Boolean, val track: RectF, val labelBaseline: Float, val valueBaseline: Float, val valueX: Float)

    private fun sliderGeom(t: DeckTile, r: RectF, labelSize: Float): SliderGeom {
        val w = r.width(); val h = r.height()
        val pad = (min(w, h) * 0.12f).coerceIn(8 * density, 18 * density)
        val hasLabel = t.label != null
        return if (h > w * 1.1f) {
            val tw = min(w * 0.42f, 64 * density)
            val top = r.top + pad + max(labelSize * 1.7f, 46 * density)
            val bottom = r.bottom - pad - if (hasLabel) labelSize * 1.7f else 0f
            SliderGeom(true, RectF(r.centerX() - tw / 2, top, r.centerX() + tw / 2, bottom),
                r.bottom - pad - labelSize * 0.3f, r.top + pad + max(labelSize, 22 * density), r.centerX())
        } else {
            val th = min(h * 0.34f, 44 * density)
            val bottom = r.bottom - pad
            SliderGeom(false, RectF(r.left + pad, bottom - th, r.right - pad, bottom),
                r.top + pad + labelSize, r.top + pad + labelSize, r.right - pad)
        }
    }

    private fun drawSliders(c: Canvas, page: DeckPage, th: Theme, dx: Float) {
        for (t in page.tiles) {
            if (t.kind != "slider") continue
            tileRect(t, page, th, rect)
            rect.offset(dx, 0f)
            val labelSize = (min(rect.height() * 0.13f, rect.width() * 0.12f)).coerceIn(10f * density, 17f * density)
            val g = sliderGeom(t, rect, labelSize)
            val v = values[t.id] ?: -1f
            val tr = g.track
            val radius = min(min(tr.width(), tr.height()) / 2f, 12 * density)
            fill.color = mix(th.tile, th.bg, 0.55f)
            c.drawRoundRect(tr, radius, radius, fill)
            val muted = mutes[t.id] == true
            if (v >= 0f) {
                val color = (t.color ?: Color.parseColor("#3CC8F0")).let { if (muted) mix(it, th.sub, 0.7f) else it }
                fill.color = color
                val f = RectF(tr)
                if (g.vertical) f.top = tr.bottom - (tr.height() * v) else f.right = tr.left + tr.width() * v
                if ((g.vertical && f.height() > 1f) || (!g.vertical && f.width() > 1f)) {
                    c.drawRoundRect(f, radius, radius, fill)

                    fill.color = mix(color, Color.WHITE, 0.55f)
                    if (g.vertical) c.drawRoundRect(RectF(f.left + 4 * density, f.top + 3 * density, f.right - 4 * density, f.top + 6 * density), 2 * density, 2 * density, fill)
                    else c.drawRoundRect(RectF(f.right - 6 * density, f.top + 4 * density, f.right - 3 * density, f.bottom - 4 * density), 2 * density, 2 * density, fill)
                }
            }
            labelPaint.textSize = labelSize * 1.15f
            labelPaint.color = if (v >= 0f && !muted) th.text else th.sub
            val text = if (v >= 0f) "${(v * 100).toInt()}" else "—"
            val tw = labelPaint.measureText(text)

            val isz = max(labelSize * 1.2f, 18 * density)
            val btnR = isz * 0.95f
            val gapI = 8 * density
            val tx = if (g.vertical) g.valueX - (tw + btnR * 2 + gapI) / 2 + btnR * 2 + gapI else g.valueX - tw
            val icx = tx - gapI - btnR
            val icy = g.valueBaseline - labelPaint.textSize * 0.36f
            muteCenters[t.id] = android.graphics.PointF(icx - dx, icy)
            if (t.sm == "bri") sunIcon(c, icx, icy, isz, th.sub)
            else {
                val red = Color.parseColor("#FF3D5A")
                fill.color = if (muted) mix(th.tile, red, 0.3f) else mix(th.tile, th.text, 0.1f)
                c.drawCircle(icx, icy, btnR, fill)
                stroke.strokeWidth = max(1f, density); stroke.color = if (muted) red else mix(th.border, th.text, 0.15f)
                c.drawCircle(icx, icy, btnR, stroke)
                muteIcon(c, icx, icy, isz * 0.72f, if (muted) red else th.text, muted, t.sm == "mic")
            }
            c.drawText(text, tx, g.valueBaseline, labelPaint)
        }
    }

    private fun drawLive(c: Canvas, page: DeckPage, th: Theme, dx: Float): Long {
        var delay = 0L
        fun pick(v: Long) { if (v > 0 && (delay == 0L || v < delay)) delay = v }
        for (t in page.tiles) {
            if (t.id in broken) { drawBroken(c, t, page, th, dx); continue }
            val saveCount = c.save()
            try {
            if (t.pulse && toggles[t.id] == true) {
                tileRect(t, page, th, pulseRect)
                pulseRect.offset(dx, 0f)
                pick(widgets.drawPulse(c, pulseRect, th, t.on?.color ?: t.color ?: th.accent))
                continue
            }
            if (t.live == null && t.media == null && !t.widget) continue
            tileRect(t, page, th, rect)
            rect.offset(dx, 0f)
            val alpha = cardAlpha(page, th)
            if (t.widget) {
                pick(when {
                    t.clock != null -> widgets.drawClock(c, t, rect, th, alpha)
                    t.timer != null -> widgets.drawTimer(c, t, rect, th, alpha)
                    t.pv != null -> widgets.drawPlugin(c, t, rect, th, alpha)
                    else -> widgets.drawWeather(c, t, rect, th, alpha)
                })
            } else if (t.live != null) {
                live.draw(c, t, rect, th, alpha)
                pick(33)
            } else {
                val anim = media.draw(c, t, rect, th, alpha)
                pick(if (anim) 16 else if (MediaStore.get(t.media!!.key)?.playing == true) 1000 else 0)
            }
            } catch (e: Exception) {
                broken.add(t.id)
                val name = t.label?.takeIf { it.isNotBlank() } ?: I18n.t("A tile")
                post { showToast(I18n.t("{0} failed and was closed", name)) }
                pick(16)
            } finally {
                c.restoreToCount(saveCount)
            }
        }
        return delay
    }

    private fun sunIcon(c: Canvas, cx: Float, cy: Float, s: Float, color: Int) {
        fill.color = color; stroke.color = color; stroke.strokeWidth = max(1.4f, s * 0.1f); stroke.strokeCap = Paint.Cap.ROUND
        c.drawCircle(cx, cy, s * 0.2f, fill)
        for (i in 0 until 8) {
            val a = Math.PI * i / 4
            val cs = Math.cos(a).toFloat(); val sn = Math.sin(a).toFloat()
            c.drawLine(cx + cs * s * 0.32f, cy + sn * s * 0.32f, cx + cs * s * 0.46f, cy + sn * s * 0.46f, stroke)
        }
        stroke.strokeCap = Paint.Cap.BUTT
    }

    private fun muteIcon(c: Canvas, cx: Float, cy: Float, s: Float, color: Int, muted: Boolean, mic: Boolean) {
        stroke.color = color; stroke.strokeWidth = max(1.4f, s * 0.11f); stroke.strokeCap = Paint.Cap.ROUND
        fill.color = color
        if (mic) {
            rect.set(cx - s * 0.16f, cy - s * 0.48f, cx + s * 0.16f, cy + s * 0.08f)
            c.drawRoundRect(rect, s * 0.16f, s * 0.16f, if (muted) stroke else fill)
            rect.set(cx - s * 0.32f, cy - s * 0.3f, cx + s * 0.32f, cy + s * 0.24f)
            c.drawArc(rect, 20f, 140f, false, stroke)
            c.drawLine(cx, cy + s * 0.24f, cx, cy + s * 0.44f, stroke)
            if (muted) c.drawLine(cx - s * 0.42f, cy - s * 0.46f, cx + s * 0.42f, cy + s * 0.42f, stroke)
        } else {
            iconPath.reset()
            iconPath.moveTo(cx - s * 0.46f, cy - s * 0.14f); iconPath.lineTo(cx - s * 0.24f, cy - s * 0.14f)
            iconPath.lineTo(cx + s * 0.02f, cy - s * 0.4f); iconPath.lineTo(cx + s * 0.02f, cy + s * 0.4f)
            iconPath.lineTo(cx - s * 0.24f, cy + s * 0.14f); iconPath.lineTo(cx - s * 0.46f, cy + s * 0.14f); iconPath.close()
            c.drawPath(iconPath, fill)
            if (muted) {
                c.drawLine(cx + s * 0.16f, cy - s * 0.17f, cx + s * 0.48f, cy + s * 0.17f, stroke)
                c.drawLine(cx + s * 0.48f, cy - s * 0.17f, cx + s * 0.16f, cy + s * 0.17f, stroke)
            } else {
                rect.set(cx - s * 0.18f, cy - s * 0.24f, cx + s * 0.3f, cy + s * 0.24f)
                c.drawArc(rect, -50f, 100f, false, stroke)
            }
        }
        stroke.strokeCap = Paint.Cap.BUTT
    }

    private fun inMuteZone(t: DeckTile, page: DeckPage, th: Theme, x: Float, y: Float): Boolean {
        tileRect(t, page, th, rect)
        val labelSize = (min(rect.height() * 0.13f, rect.width() * 0.12f)).coerceIn(10f * density, 17f * density)
        val g = sliderGeom(t, rect, labelSize)

        muteCenters[t.id]?.let { if (kotlin.math.hypot(x - it.x, y - it.y) < 34 * density) return true }
        return if (g.vertical) y < g.track.top - 2 * density
        else y < g.track.top - 2 * density && x > rect.centerX()
    }

    private fun updateSlider(t: DeckTile, page: DeckPage, th: Theme, x: Float, y: Float, final: Boolean) {
        tileRect(t, page, th, rect)
        val labelSize = (min(rect.height() * 0.13f, rect.width() * 0.12f)).coerceIn(10f * density, 17f * density)
        val g = sliderGeom(t, rect, labelSize)
        val v = (if (g.vertical) (g.track.bottom - y) / g.track.height() else (x - g.track.left) / g.track.width()).coerceIn(0f, 1f)
        val now = SystemClock.uptimeMillis()
        values[t.id] = v
        lastLocal[t.id] = now
        if (mutes[t.id] == true) { mutes[t.id] = false; muteLocal[t.id] = now }
        pendingValues.remove(t.id)
        if (t.sm == "bri") { listener?.onBrightness(v); invalidate(); return }
        if (final || now - lastSlideSent >= 33) {
            lastSlideSent = now
            listener?.onSlide(t, v)
        }
        invalidate()
    }

    private fun drawText(c: Canvas, text: String, p: TextPaint, x: Float, baseline: Float, maxW: Float, center: Boolean) {
        val s = TextUtils.ellipsize(text, p, maxW, TextUtils.TruncateAt.END).toString()
        val dx = if (center) -p.measureText(s) / 2f else 0f
        c.drawText(s, x + dx, baseline, p)
    }

    private fun drawPageDots(c: Canvas, page: DeckPage, l: Layout) {
        if (page.folder) return
        val pages = l.swipePages
        if (pages.size < 2) return
        val idx = pages.indexOfFirst { it.id == page.id }
        val r = 2.5f * density
        val step = 12f * density
        val y = height - l.theme.pad / 2f
        val x0 = width / 2f - (pages.size - 1) * step / 2f
        for (i in pages.indices) {
            fill.color = if (i == idx) l.theme.text else mix(l.theme.bg, l.theme.sub, 0.5f)
            c.drawCircle(x0 + i * step, y, if (i == idx) r * 1.2f else r, fill)
        }
    }

    override fun onDraw(canvas: Canvas) {
        val l = layout
        if (l == null) { canvas.drawColor(Color.parseColor("#07090E")); return }
        val page = l.page(pageId) ?: return
        val now = SystemClock.uptimeMillis()
        val layer = layerFor(page) ?: return
        var animating = false

        var gifWait = 0L
        drawBackground(canvas, page, l.theme)
        if (dragX != 0f) {
            canvas.drawBitmap(layer, dragX, 0f, null)
            drawSliders(canvas, page, l.theme, dragX)
            drawLive(canvas, page, l.theme, dragX)
            drawGifs(canvas, page, l.theme, dragX, now)
            neighbor(if (dragX < 0) 1 else -1)?.let { n ->
                val ox = dragX + if (dragX < 0) width else -width
                layerFor(n)?.let { canvas.drawBitmap(it, ox, 0f, null) }
                drawSliders(canvas, n, l.theme, ox)
                drawLive(canvas, n, l.theme, ox)
                drawGifs(canvas, n, l.theme, ox, now)
            }
        } else {
            presses.removeAll { it.pageId != page.id || (it.upAt >= 0 && pressAmount(it, now) <= 0f) }

            val pressedHere = presses.filter { it.pageId == page.id && it.tile.gif == null }
            if (pressedHere.isEmpty()) canvas.drawBitmap(layer, 0f, 0f, null)
            else {
                canvas.save()
                for (p in pressedHere) {
                    tileRect(p.tile, page, l.theme, rect)
                    @Suppress("DEPRECATION")
                    canvas.clipRect(rect.left - 1, rect.top - 1, rect.right + 1, rect.bottom + 1, Region.Op.DIFFERENCE)
                }
                canvas.drawBitmap(layer, 0f, 0f, null)
                canvas.restore()
            }
            drawSliders(canvas, page, l.theme, 0f)
            liveDelay = drawLive(canvas, page, l.theme, 0f)
            gifWait = drawGifs(canvas, page, l.theme, 0f, now)

            fadeFrom?.let { old ->
                val k = (now - fadeStart) / 160f
                if (k >= 1f || old.isRecycled) fadeFrom = null else {
                    bmpPaint.alpha = ((1f - k) * 255).toInt()
                    canvas.drawBitmap(old, 0f, 0f, bmpPaint)
                    bmpPaint.alpha = 255
                    animating = true
                }
            }

            val it = presses.iterator()
            while (it.hasNext()) {
                val p = it.next()
                if (p.pageId != page.id) { it.remove(); continue }
                val k = pressAmount(p, now)
                if (k <= 0f && p.upAt >= 0) { it.remove(); continue }
                animating = true
                if (p.tile.gif == null) drawPressed(canvas, layer, p.tile, page, l.theme, k)
            }
        }

        if (toast != null) {
            if (now > toastUntil) toast = null else { drawToast(canvas, l.theme); animating = true }
        }
        val wait = listOf(liveDelay, gifWait).filter { it > 0 }.minOrNull() ?: 0L
        if (animating) postInvalidateOnAnimation()
        else if (wait > 0) postInvalidateDelayed(wait)
    }

    private var liveDelay = 0L

    private fun gifFrameAt(t: DeckTile, now: Long): Pair<Int, Long> {
        val g = t.gif ?: return 0 to 0L
        val start: Long = when (g.play) {
            "press" -> gifPressAt[t.id] ?: return 0 to 0L
            "once" -> {
                val s = gifOnceAt[t.id] ?: return 0 to 0L
                if (now - s >= g.total) { gifOnceAt.remove(t.id); return 0 to 0L }
                s
            }
            else -> 0L
        }
        var e = if (g.play == "once") (now - start).toInt() else ((now - start) % g.total).toInt()
        for (i in 0 until g.frames) {
            val d = g.delays.getOrElse(i) { 100 }
            if (e < d) return i to (d - e).toLong().coerceAtLeast(10)
            e -= d
        }
        return 0 to 16L
    }

    private fun drawGifs(c: Canvas, page: DeckPage, th: Theme, dx: Float, now: Long): Long {
        var wait = 0L
        for (t in page.tiles) {
            val g = t.gif ?: continue
            val frames = GifStore.frames(g)
            val (idx, w) = gifFrameAt(t, now)
            if (frames != null && w > 0 && (wait == 0L || w < wait)) wait = w
            val frame = frames?.getOrNull(idx)
            val p = if (dx == 0f) presses.lastOrNull { it.tile === t && it.pageId == page.id } else null
            val k = if (p != null) pressAmount(p, now) else 0f
            c.save()
            c.translate(dx, 0f)
            if (k > 0f) {
                tileRect(t, page, th, rect)
                val s = 1f - 0.07f * k
                c.scale(s, s, rect.centerX(), rect.centerY())
            }
            drawTile(c, t, page, th, frame)
            if (k > 0f) {
                tileRect(t, page, th, rect)
                fill.color = t.color ?: th.press; fill.alpha = (70 * k).toInt()
                c.drawRoundRect(rect, th.radius, th.radius, fill); fill.alpha = 255
                stroke.color = t.color ?: th.press; stroke.alpha = (255 * k).toInt(); stroke.strokeWidth = 2f * density
                c.drawRoundRect(rect, th.radius, th.radius, stroke); stroke.alpha = 255
            }
            c.restore()
        }
        return wait
    }

    private fun pressAmount(p: Press, now: Long): Float {
        val down = ((now - p.downAt) / 70f).coerceIn(0f, 1f)
        if (p.upAt < 0) return down
        val peak = ((p.upAt - p.downAt) / 70f).coerceIn(0.6f, 1f)
        return (1f - (now - p.upAt) / 160f).coerceIn(0f, 1f) * peak
    }

    private fun drawPressed(c: Canvas, layer: Bitmap, t: DeckTile, page: DeckPage, th: Theme, k: Float) {
        tileRect(t, page, th, rect)

        src.set(rect.left.toInt(), rect.top.toInt(), rect.right.toInt(), rect.bottom.toInt())
        val s = 1f - 0.07f * k
        val cx = rect.centerX(); val cy = rect.centerY()
        val hw = rect.width() * s / 2f; val hh = rect.height() * s / 2f
        rect.set(cx - hw, cy - hh, cx + hw, cy + hh)
        c.drawBitmap(layer, src, rect, bmpPaint)

        fill.color = (t.color ?: th.press)
        fill.alpha = (70 * k).toInt()
        val r = th.radius * s
        c.drawRoundRect(rect, r, r, fill)
        fill.alpha = 255
        stroke.color = t.color ?: th.press
        stroke.alpha = (255 * k).toInt()
        stroke.strokeWidth = 2f * density
        c.drawRoundRect(rect, r, r, stroke)
        stroke.alpha = 255
    }

    private fun drawToast(c: Canvas, th: Theme) {
        val text = toast ?: return
        labelPaint.textSize = 14f * density
        labelPaint.color = th.text
        val tw = labelPaint.measureText(text)
        val padH = 18f * density
        val h = 40f * density
        rect.set(width / 2f - tw / 2f - padH, height - th.pad - h - 18f * density, width / 2f + tw / 2f + padH, height - th.pad - 18f * density)
        fill.color = mix(th.tile, th.press, 0.25f)
        c.drawRoundRect(rect, h / 2, h / 2, fill)
        c.drawText(text, rect.centerX() - tw / 2f, rect.centerY() + labelPaint.textSize / 3f, labelPaint)
    }

    private fun neighbor(dir: Int): DeckPage? {
        val l = layout ?: return null
        val current = l.page(pageId) ?: return null
        if (current.folder) return null
        val pages = l.swipePages
        val i = pages.indexOfFirst { it.id == current.id }
        return pages.getOrNull(i + dir)
    }

    private fun goTo(id: String, pushFolder: Boolean) {
        val l = layout ?: return
        val target = l.page(id) ?: return
        val currentLayer = l.page(pageId)?.let { layers[it.id] }
        if (pushFolder) pageId?.let { folderStack.add(it) }
        if (!target.folder && !pushFolder) folderStack.clear()
        pageId = target.id
        notifyWall()
        fadeFrom = currentLayer
        fadeStart = SystemClock.uptimeMillis()
        invalidate()
    }

    private fun back() {
        val prev = if (folderStack.isNotEmpty()) folderStack.removeAt(folderStack.size - 1) else layout?.start
        if (prev != null) goTo(prev, pushFolder = false)
    }

    fun onBack(): Boolean {
        val page = layout?.page(pageId) ?: return false
        if (!page.folder) return false
        back()
        return true
    }

    private fun slideTo(dir: Int) {
        val target = if (dir == 0) null else neighbor(dir)
        val from = dragX
        val to = if (target == null) 0f else if (dir > 0) -width.toFloat() else width.toFloat()
        slideAnim?.cancel()
        slideAnim = ValueAnimator.ofFloat(from, to).apply {
            duration = (180 * abs(to - from) / max(1f, width.toFloat())).toLong().coerceIn(80, 220)
            interpolator = DecelerateInterpolator(1.6f)
            addUpdateListener { dragX = it.animatedValue as Float; invalidate() }
            addListener(object : android.animation.AnimatorListenerAdapter() {
                override fun onAnimationEnd(animation: android.animation.Animator) {
                    if (target != null && to != 0f) pageId = target.id
                    dragX = 0f
                    notifyWall()
                    invalidate()
                }
            })
            start()
        }
    }

    var brightness = 0.8f
        set(v) { field = v; layout?.pages?.forEach { p -> p.tiles.filter { it.sm == "bri" }.forEach { values[it.id] = v } }; invalidate() }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        if (e.actionMasked == MotionEvent.ACTION_DOWN) listener?.onAnyTouch()
        val l = layout ?: return true
        val page = l.page(pageId) ?: return true
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                slideAnim?.cancel()
                downX = e.x; downY = e.y
                swiping = false
                val hit = tileAt(page, l.theme, e.x, e.y)
                muteDown = null
                if (hit?.kind == "slider" && hit.sm != "bri" && inMuteZone(hit, page, l.theme, e.x, e.y)) {
                    muteDown = hit
                    downTile = null
                    parent?.requestDisallowInterceptTouchEvent(true)
                    return true
                }
                if (hit?.kind == "slider") {
                    sliderDrag = hit
                    downTile = null
                    parent?.requestDisallowInterceptTouchEvent(true)
                    updateSlider(hit, page, l.theme, e.x, e.y, final = false)
                    return true
                }
                mediaDown = null; mediaHit = null
                if (hit?.media != null) {
                    media.hit(hit, e.x, e.y)?.let { mediaDown = hit; mediaHit = it; invalidate() }
                }
                timerDown = if (hit?.timer != null) hit else null
                downTile = if (hit?.live != null || hit?.media != null || hit?.widget == true) null else hit
                held = false
                cancelGesture()
                downTile?.let {
                    if (it.gif?.play == "press") gifPressAt[it.id] = SystemClock.uptimeMillis()
                    presses.add(Press(it, page.id, SystemClock.uptimeMillis()))
                    if (it.hold) { holdTile = it; postDelayed(holdRunnable, holdMs) }
                    invalidate()
                }
            }
            MotionEvent.ACTION_MOVE -> {
                if (muteDown != null) return true
                sliderDrag?.let { updateSlider(it, page, l.theme, e.x, e.y, final = false); return true }
                val dx = e.x - downX
                if (!swiping && abs(dx) > 18 * density && abs(dx) > abs(e.y - downY) && !page.folder && l.swipePages.size > 1) {
                    swiping = true; mediaDown = null; mediaHit = null; timerDown = null
                    cancelPress()
                }
                if (swiping) {
                    val dir = if (dx < 0) 1 else -1
                    dragX = if (neighbor(dir) == null) dx * 0.25f else dx
                    invalidate()
                } else if (downTile != null && tileAt(page, l.theme, e.x, e.y) != downTile) {
                    cancelPress()
                }
            }
            MotionEvent.ACTION_UP -> {
                muteDown?.let {
                    muteDown = null
                    if (abs(e.x - downX) < 16 * density && abs(e.y - downY) < 16 * density) {
                        mutes[it.id] = mutes[it.id] != true
                        muteLocal[it.id] = SystemClock.uptimeMillis()
                        listener?.onSlideMute(it)
                        invalidate()
                    }
                    return true
                }
                sliderDrag?.let {
                    updateSlider(it, page, l.theme, e.x, e.y, final = true)
                    sliderDrag = null
                    return true
                }
                if (swiping) {
                    val dx = e.x - downX
                    slideTo(if (abs(dx) > width * 0.18f) (if (dx < 0) 1 else -1) else 0)
                } else {
                    val mt = mediaDown
                    val mh = mediaHit
                    if (mt != null && mh != null) {
                        val key = mt.media!!.key
                        val cmd = mh.first
                        var v = mh.second
                        if (cmd == "seek") media.hit(mt, e.x, e.y)?.takeIf { it.first == "seek" }?.let { v = it.second }
                        when (cmd) {
                            "play" -> MediaStore.optimisticToggle(key)
                            "seek" -> MediaStore.optimisticSeek(key, v)
                        }
                        listener?.onMedia(mt, cmd, v)
                    }
                    mediaDown = null; mediaHit = null
                    timerDown?.let { tt ->
                        tileRect(tt, page, l.theme, rect)
                        if (rect.contains(e.x, e.y)) {
                            when (widgets.timerHit(tt, rect, downX, downY)) {
                                "reset" -> TimerStore.reset(tt.id)
                                "plus" -> TimerStore.addMinute(tt.id)
                                else -> TimerStore.toggle(tt.id)
                            }
                        }
                    }
                    timerDown = null
                    val t = downTile
                    cancelGesture()
                    presses.lastOrNull { it.tile === t && it.upAt < 0 }?.upAt = SystemClock.uptimeMillis()
                    if (t != null && !held) release(t)
                    held = false
                    invalidate()
                }
                downTile = null
            }
            MotionEvent.ACTION_CANCEL -> {
                muteDown = null
                timerDown = null
                mediaDown = null; mediaHit = null
                sliderDrag = null
                cancelPress()
                if (swiping) slideTo(0)
                downTile = null
            }
        }
        return true
    }

    private fun cancelPress() {
        cancelGesture()
        gifPressAt.clear()
        val now = SystemClock.uptimeMillis()
        presses.forEach { if (it.upAt < 0) it.upAt = now }
        downTile = null
        invalidate()
    }

    private fun activate(t: DeckTile) {
        gifPressAt.remove(t.id)
        if (t.gif?.play == "once") gifOnceAt[t.id] = SystemClock.uptimeMillis()
        when (t.nav) {
            "page" -> t.target?.let { goTo(it, pushFolder = false) }
            "folder" -> t.target?.let { goTo(it, pushFolder = true) }
            "back" -> back()
            else -> listener?.onPress(t, "tap")
        }
    }
}
