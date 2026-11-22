package app.mydeck

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.os.SystemClock
import android.util.Base64
import org.json.JSONObject

object MediaStore {
    class Info(
        val app: String, val title: String, val artist: String, val album: String,
        var playing: Boolean, val pos: Float, var posAt: Long, val dur: Float,
        val canSeek: Boolean, val canNext: Boolean, val canPrev: Boolean,
        val tint: Int, val rev: Int, var art: Bitmap?,
    ) {
        var basePos = pos
        fun position(): Float {
            val p = if (playing) basePos + (SystemClock.uptimeMillis() - posAt) / 1000f else basePos
            return if (dur > 0) p.coerceIn(0f, dur) else maxOf(0f, p)
        }
        val empty get() = title.isEmpty()
    }

    private val infos = HashMap<String, Info>()

    var onChange: (() -> Unit)? = null

    private val prevArt = HashMap<String, Bitmap>()
    private val artChangedAt = HashMap<String, Long>()

    fun get(key: String): Info? = infos[key]
    fun previousArt(key: String): Bitmap? = prevArt[key]
    fun artAge(key: String): Long = SystemClock.uptimeMillis() - (artChangedAt[key] ?: 0L)

    class Parsed(val key: String, val info: Info, val hasArt: Boolean)

    fun parse(o: JSONObject): Parsed {
        var art: Bitmap? = null
        val b64 = if (o.isNull("art")) "" else o.optString("art", "")
        if (b64.isNotEmpty()) {
            art = try {
                val bytes = Base64.decode(b64, Base64.DEFAULT)
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
            } catch (_: Exception) { null }
        }
        val tint = try { Color.parseColor(o.optString("tint", "#FF3D5A")) } catch (_: Exception) { Color.parseColor("#FF3D5A") }
        val info = Info(
            app = o.optString("app"), title = o.optString("title"), artist = o.optString("artist"), album = o.optString("album"),
            playing = o.optBoolean("playing"), pos = o.optDouble("pos", 0.0).toFloat(), posAt = SystemClock.uptimeMillis(),
            dur = o.optDouble("dur", 0.0).toFloat(), canSeek = o.optBoolean("seek"), canNext = o.optBoolean("next", true),
            canPrev = o.optBoolean("prev", true), tint = tint, rev = o.optInt("rev"), art = art,
        )
        return Parsed(o.optString("k", ""), info, b64.isNotEmpty())
    }

    fun apply(p: Parsed) {
        val old = infos[p.key]
        val info = p.info
        info.posAt = SystemClock.uptimeMillis()

        val sameSong = old != null && old.app == info.app && old.title == info.title &&
            old.artist == info.artist && old.album == info.album
        if (sameSong && old != null && old.playing && info.playing && seekAt + 3000 < info.posAt) {
            val mine = old.position()
            if (kotlin.math.abs(mine - info.pos) < 3.5f) { info.basePos = mine; info.posAt = SystemClock.uptimeMillis() }
        }
        if (sameSong && !p.hasArt && old != null && old.rev == info.rev) info.art = old.art
        if (!sameSong || info.art == null) {
            prevArt.remove(p.key)
            artChangedAt.remove(p.key)
        } else if (old?.art != null && old.rev != info.rev) {
            prevArt[p.key] = old.art!!
            artChangedAt[p.key] = SystemClock.uptimeMillis()
        }
        infos[p.key] = info
        onChange?.invoke()
    }

    fun optimisticToggle(key: String) {
        val i = infos[key] ?: return
        i.basePos = i.position()
        i.posAt = SystemClock.uptimeMillis()
        i.playing = !i.playing
    }

    private var seekAt = 0L

    fun optimisticSeek(key: String, sec: Float) {
        seekAt = SystemClock.uptimeMillis()
        val i = infos[key] ?: return
        i.basePos = sec
        i.posAt = SystemClock.uptimeMillis()
    }
}
