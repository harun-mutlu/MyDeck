package app.mydeck

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import kotlin.math.PI
import kotlin.math.cos
import kotlin.math.min
import kotlin.math.sin
import kotlin.random.Random

internal class WeatherArtwork(context: Context) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val bounds = RectF()
    private val kinds = listOf("sun", "moon", "cloudsun", "cloudmoon", "cloud", "rain", "sleet", "snow", "storm", "fog", "wind")
    private val sheet = runCatching {
        context.assets.open("weather-art.png").use { BitmapFactory.decodeStream(it) }
    }.getOrNull()
    private val art = sheet?.let { image ->
        kinds.mapIndexed { index, kind ->
            val top = intArrayOf(0, 366, 708)[index / 4]
            val height = intArrayOf(287, 258, 270)[index / 4]
            val cutoff = if (kind == "storm") 158 else 147
            layer(image, index % 4 * 362, top, 362, height) { x, y, color ->
                when (kind) {
                    "sun" -> ((132 - kotlin.math.hypot(x - 194f, y - 174f)) / 47).coerceIn(0f, 1f)
                    "rain", "sleet", "snow", "storm" -> ((cutoff + 11f - y) / 20).coerceIn(0f, 1f)
                    "wind" -> 1f - warm(color)
                    else -> 1f
                }
            }
        }
    }
    private val drops = sheet?.let { image ->
        listOf(layer(image, 553, 546, 16, 32), layer(image, 602, 514, 16, 34), layer(image, 432, 574, 20, 34))
    }
    private val bolt = sheet?.let { image ->
        layer(image, 0, 708, 362, 270) { _, y, color -> if (y < 138) 0f else warm(color) }
    }
    private val leaves = sheet?.let { image ->
        listOf(
            layer(image, 942, 764, 84, 86) { _, _, color -> warm(color) },
            layer(image, 801, 857, 85, 70) { _, _, color -> warm(color) },
        )
    }

    private fun warm(color: Int): Float = ((Color.red(color) - Color.blue(color) - 8) / 28f).coerceIn(0f, 1f)

    private fun layer(
        image: Bitmap, left: Int, top: Int, width: Int, height: Int,
        mask: (Int, Int, Int) -> Float = { _, _, _ -> 1f },
    ): Bitmap {
        val pixels = IntArray(width * height)
        image.getPixels(pixels, 0, width, left, top, width, height)
        for (index in pixels.indices) {
            val color = pixels[index]
            val strength = maxOf(Color.red(color) - 13, Color.green(color) - 20, Color.blue(color) - 33)
            val alpha = ((strength - 3) / 35f).coerceIn(0f, 1f) * mask(index % width, index / width, color)
            pixels[index] = (color and 0xFFFFFF) or ((alpha * 255).toInt() shl 24)
        }
        return Bitmap.createBitmap(pixels, width, height, Bitmap.Config.ARGB_8888)
    }

    private class Particle(val snow: Boolean, val start: Long, val duration: Long)
    private val schedules = HashMap<String, Array<Particle>>()
    private var nextFlash = 0L

    fun draw(c: Canvas, kind: String, cx: Float, cy: Float, size: Float, opacity: Float, now: Long): Boolean {
        val images = art ?: return false
        if (size <= 0f) return true
        val index = kinds.indexOf(kind).takeIf { it >= 0 } ?: 4
        val image = images[index]
        val scale = size / image.width
        val height = image.height * scale
        val left = cx - size / 2
        val top = cy - height / 2
        c.save()
        c.clipRect(left, cy - size / 2, left + size, cy + size / 2)
        if (kind == "sun") {
            c.rotate((now % 22000) * 360f / 22000, left + 194 * scale, top + 174 * scale)
        }
        val pulse = when (kind) {
            "moon", "cloudsun", "cloudmoon", "cloud", "fog", "wind" ->
                0.89f + 0.11f * cos((now % 7000) * 2 * PI / 7000).toFloat()
            else -> 1f
        }
        drawBitmap(c, image, left, top, size, height, opacity * pulse)
        c.restore()

        when (kind) {
            "rain", "sleet", "snow", "storm" -> {
                c.save()
                c.clipRect(left, top, left + size, top + height)
                if (kind == "storm") {
                    if (nextFlash == 0L || now > nextFlash + 240) nextFlash = now + Random.nextLong(2300, 5700)
                    val age = now - nextFlash
                    val flash = when (age) { in 0L..104L -> 1f; in 155L..224L -> 0.63f; else -> 0f }
                    if (flash > 0f) bolt?.let {
                        drawBitmap(c, it, left - size * .09f, top - height * .15f, size * 1.18f, height * 1.15f, opacity * flash)
                    }
                }
                precipitation(c, kind, left, top, size, scale, opacity, now, image.height)
                c.restore()
            }
            "wind" -> leaves?.forEachIndexed { index, leaf ->
                val phase = ((now + index * 1700) % 3900) / 3900f
                val x = left + size * (-0.12f + 1.24f * phase)
                val y = top + scale * (95 + index * 46 - phase * 30)
                val w = leaf.width * scale * .7f
                val h = leaf.height * scale * .7f
                c.save()
                c.clipRect(left, top, left + size, top + height)
                c.rotate(phase * 170 + index * 50, x, y)
                drawBitmap(c, leaf, x - w / 2, y - h / 2, w, h, opacity * minOf(1f, phase * 14, (1 - phase) * 14))
                c.restore()
            }
        }
        return true
    }

    private fun particle(snow: Boolean, start: Long) =
        Particle(snow, start, if (snow) Random.nextLong(1300, 2200) else Random.nextLong(470, 1020))

    private fun precipitation(c: Canvas, kind: String, left: Float, top: Float, size: Float, scale: Float, opacity: Float, now: Long, height: Int) {
        val count = when (kind) { "rain" -> 11; "sleet" -> 10; else -> 8 }
        val particles = schedules.getOrPut(kind) {
            Array(count) { particle(kind == "snow" || kind == "sleet" && it % 2 == 0, now + Random.nextLong(1100)) }
        }
        val start = if (kind == "storm") 158 else 147
        particles.forEachIndexed { i, p ->
            val phase = (now - p.start).toFloat() / p.duration
            if (phase < 0f) return@forEachIndexed
            if (phase >= 1f) {
                particles[i] = particle(p.snow, now + Random.nextLong(100, 1100))
                return@forEachIndexed
            }
            val x = left + size * (.16f + .68f * (i + .5f) / count)
            val y = top + scale * (start - 5 + phase * (height - start - 8))
            val alpha = opacity * minOf(1f, phase * 16, (1 - phase) * 10)
            if (p.snow) snowflake(c, x, y, scale * (13.65f + i % 3 * 1.575f), alpha)
            else drops?.let {
                val drop = it[i % it.size]
                val factor = scale * (.94f + i % 3 * .08f)
                drawBitmap(c, drop, x - drop.width * factor / 2, y, drop.width * factor, drop.height * factor, alpha)
            }
        }
    }

    private fun snowflake(c: Canvas, x: Float, y: Float, radius: Float, opacity: Float) {
        paint.color = 0xFFF2FBFF.toInt()
        paint.alpha = (opacity * 255).toInt()
        paint.strokeWidth = maxOf(0.8f, radius * .14f)
        paint.strokeCap = Paint.Cap.ROUND
        for (arm in 0..5) {
            val angle = arm * PI / 3
            val dx = cos(angle).toFloat() * radius
            val dy = sin(angle).toFloat() * radius
            c.drawLine(x, y, x + dx, y + dy, paint)
            for (joint in listOf(.48f, .76f)) {
                val jx = x + dx * joint
                val jy = y + dy * joint
                c.drawLine(jx, jy, jx + dx * .18f - dy * .22f, jy + dy * .18f + dx * .22f, paint)
                c.drawLine(jx, jy, jx + dx * .18f + dy * .22f, jy + dy * .18f - dx * .22f, paint)
            }
        }
    }

    private fun drawBitmap(c: Canvas, bitmap: Bitmap, x: Float, y: Float, w: Float, h: Float, opacity: Float) {
        paint.alpha = (opacity.coerceIn(0f, 1f) * 255).toInt()
        bounds.set(x, y, x + w, y + h)
        c.drawBitmap(bitmap, null, bounds, paint)
    }
}
