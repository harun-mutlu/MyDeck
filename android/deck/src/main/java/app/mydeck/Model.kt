package app.mydeck

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.util.Base64
import org.json.JSONObject

class Layout(
    val rev: Int,
    val start: String,
    val theme: Theme,
    val pages: List<DeckPage>,

    val idleSec: Int = 600,
    val sleepWhenPcOff: Boolean = true,
) {
    fun page(id: String?): DeckPage? = pages.firstOrNull { it.id == id }

    val swipePages: List<DeckPage> get() = pages.filter { !it.folder }

    companion object {
        fun parse(o: JSONObject): Layout {
            val t = o.getJSONObject("theme")
            val theme = Theme(
                bg = color(t, "bg", "#07090E"),
                tile = color(t, "tile", "#11151F"),
                border = color(t, "border", "#222938"),
                text = color(t, "text", "#E8ECF4"),
                sub = color(t, "sub", "#8D97AB"),
                accent = color(t, "accent", "#FF3D5A"),
                press = color(t, "press", "#FF3D5A"),
                radius = t.optDouble("radius", 16.0).toFloat(),
                gap = t.optDouble("gap", 12.0).toFloat(),
                pad = t.optDouble("pad", 12.0).toFloat(),
                wall = wall(t),
                dim = t.optDouble("dim", 0.2).toFloat(),
                glass = t.optDouble("glass", 0.4).toFloat(),
            )
            val pagesJson = o.getJSONArray("pages")
            val pages = ArrayList<DeckPage>(pagesJson.length())
            for (i in 0 until pagesJson.length()) {
                val p = pagesJson.getJSONObject(i)
                val tilesJson = p.getJSONArray("tiles")
                val tiles = ArrayList<DeckTile>(tilesJson.length())
                for (j in 0 until tilesJson.length()) {
                    val tj = tilesJson.getJSONObject(j)
                    val icon = bitmap(tj, "icon")
                    val onJson = if (tj.isNull("on")) null else tj.optJSONObject("on")
                    val on = onJson?.let {
                        TileVariant(str(it, "label"), str(it, "sub"), optColor(it, "color"), bitmap(it, "icon"))
                    }
                    tiles.add(
                        DeckTile(
                            id = tj.getString("id"),
                            kind = tj.optString("kind", "button"),
                            on = on,
                            x = tj.getInt("x"), y = tj.getInt("y"),
                            w = tj.optInt("w", 1), h = tj.optInt("h", 1),
                            label = str(tj, "label"),
                            sub = str(tj, "sub"),
                            color = optColor(tj, "color"),
                            bg = optColor(tj, "bg"),
                            nav = str(tj, "nav"),
                            target = str(tj, "target"),
                            sm = str(tj, "sm"),
                            icon = icon,
                            live = if (tj.isNull("live")) null else tj.optJSONObject("live")?.let { parseLive(it) },
                            gif = if (tj.isNull("gif")) null else tj.optJSONObject("gif")?.let { g ->
                                val da = g.optJSONArray("d")
                                val n = g.optInt("n", 1)
                                GifSpec(
                                    url = g.optString("url"), frames = n, w = g.optInt("w"), h = g.optInt("h"),
                                    delays = IntArray(n) { i -> da?.optInt(i, 100) ?: 100 },
                                    fill = g.optString("fill", "bg"), play = g.optString("play", "always"),
                                )
                            },
                            media = if (tj.isNull("media")) null else tj.optJSONObject("media")?.let {
                                MediaSpec(it.optString("k", ""), it.optBoolean("tint", true), it.optBoolean("ctl", true), it.optBoolean("times", true))
                            },
                            clock = if (tj.isNull("clock")) null else tj.optJSONObject("clock")?.let {
                                ClockSpec(it.optString("style", "digital"), it.optBoolean("sec", true), it.optBoolean("date", true), str(it, "tz"), str(it, "city"))
                            },
                            timer = if (tj.isNull("timer")) null else tj.optJSONObject("timer")?.let {
                                TimerSpec(it.optString("mode", "countdown"), it.optInt("secs", 300).coerceAtLeast(1))
                            },
                            wx = if (tj.isNull("wx")) null else tj.optJSONObject("wx")?.let { WxSpec(it.optString("view", "now")) },
                            pulse = tj.optBoolean("pulse", false),
                            pv = if (tj.isNull("pv")) null else tj.optJSONObject("pv")?.optString("name", I18n.t("Plugin")),
                            hold = tj.optBoolean("hold", false),
                            dbl = tj.optBoolean("dbl", false),
                        )
                    )
                }
                pages.add(
                    DeckPage(
                        id = p.getString("id"),
                        name = p.optString("name", ""),
                        cols = p.getInt("cols"),
                        rows = p.getInt("rows"),
                        folder = p.optBoolean("folder", false),
                        square = p.optBoolean("sq", false),
                        wall = wall(p),
                        tiles = tiles,
                    )
                )
            }
            val tab = o.optJSONObject("tablet")
            return Layout(o.optInt("rev", 0), o.optString("start", pages.firstOrNull()?.id ?: ""), theme, pages,
                idleSec = tab?.optInt("idle", 600) ?: 600, sleepWhenPcOff = tab?.optBoolean("pcoff", true) ?: true)
        }

        private fun wall(o: JSONObject): Wall? {
            if (o.isNull("wall")) return null
            val w = o.optJSONObject("wall") ?: return null
            val url = w.optString("url", "")
            if (url.isEmpty()) return null
            return Wall(w.optString("type", "image"), url, w.optString("name", url.substringAfterLast('/')))
        }

        private fun parseLive(o: JSONObject): LiveSpec {
            fun list(key: String): List<JSONObject> {
                val a = o.optJSONArray(key) ?: return emptyList()
                return (0 until a.length()).mapNotNull { a.optJSONObject(it) }
            }
            val series = list("s").map {
                Series(
                    key = it.optString("k"), name = it.optString("name"), unit = it.optString("unit"),
                    dec = it.optInt("dec", 0), max = it.optDouble("max", 0.0).toFloat(), min = it.optDouble("min", 0.0).toFloat(),
                    color = optColor(it, "color") ?: Color.parseColor("#FF3D5A"),
                    dash = it.optBoolean("dash", false), loadable = it.optBoolean("load", false),
                )
            }
            val info = list("info").map {
                InfoItem(it.optString("k"), it.optString("name"), it.optString("unit"), it.optInt("dec", 0), it.optDouble("max", 0.0).toFloat())
            }
            return LiveSpec(
                style = o.optString("style", "area"), colorMode = o.optString("cm", "card"),
                seconds = o.optInt("sec", 60).coerceIn(30, 300), detail = str(o, "detail"),
                series = series, info = info,
            )
        }

        private fun bitmap(o: JSONObject, key: String): Bitmap? {
            if (o.isNull(key)) return null
            val b64 = o.optString(key, "")
            if (b64.isEmpty()) return null
            return try {
                val bytes = Base64.decode(b64, Base64.DEFAULT)
                BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
            } catch (_: IllegalArgumentException) { null }
        }

        private fun str(o: JSONObject, key: String): String? =
            if (o.isNull(key)) null else o.optString(key, "").ifEmpty { null }

        private fun color(o: JSONObject, key: String, def: String): Int =
            try { Color.parseColor(o.optString(key, def)) } catch (_: IllegalArgumentException) { Color.parseColor(def) }

        private fun optColor(o: JSONObject, key: String): Int? {
            val s = str(o, key) ?: return null
            return try { Color.parseColor(s) } catch (_: IllegalArgumentException) { null }
        }
    }
}

class Theme(
    val bg: Int, val tile: Int, val border: Int, val text: Int, val sub: Int,
    val accent: Int, val press: Int, val radius: Float, val gap: Float, val pad: Float,

    val wall: Wall? = null, val dim: Float = 0.2f, val glass: Float = 0.4f,
)

class Wall(val type: String, val url: String, val name: String)

class DeckPage(
    val id: String, val name: String, val cols: Int, val rows: Int,
    val folder: Boolean, val tiles: List<DeckTile>,
    val wall: Wall? = null,

    val square: Boolean = false,
)

class TileVariant(val label: String?, val sub: String?, val color: Int?, val icon: Bitmap?)

class DeckTile(
    val id: String, val kind: String, val on: TileVariant?,
    val x: Int, val y: Int, val w: Int, val h: Int,
    val label: String?, val sub: String?, val color: Int?, val bg: Int?,
    val nav: String?, val target: String?, val icon: Bitmap?,

    val live: LiveSpec? = null,

    val media: MediaSpec? = null,

    val gif: GifSpec? = null,

    val sm: String? = null,

    val clock: ClockSpec? = null,
    val timer: TimerSpec? = null,
    val wx: WxSpec? = null,

    val pulse: Boolean = false,

    val pv: String? = null,

    val hold: Boolean = false,

    val dbl: Boolean = false,
) {
    val widget: Boolean get() = clock != null || timer != null || wx != null || pv != null
}

class ClockSpec(val style: String, val sec: Boolean, val date: Boolean, val tz: String?, val city: String?)

class TimerSpec(val mode: String, val secs: Int)

class WxSpec(val view: String)

class GifSpec(val url: String, val frames: Int, val w: Int, val h: Int, val delays: IntArray, val fill: String, val play: String) {
    val total: Int = delays.sum().coerceAtLeast(1)
}

class MediaSpec(val key: String, val followArt: Boolean, val controls: Boolean, val times: Boolean)

class Series(
    val key: String, val name: String, val unit: String, val dec: Int,

    val max: Float, val min: Float, val color: Int, val dash: Boolean, val loadable: Boolean,
)

class InfoItem(val key: String, val name: String, val unit: String, val dec: Int, val max: Float)

class LiveSpec(
    val style: String, val colorMode: String, val seconds: Int, val detail: String?,
    val series: List<Series>, val info: List<InfoItem>,
)
