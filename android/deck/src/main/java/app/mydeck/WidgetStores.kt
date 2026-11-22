package app.mydeck

import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import org.json.JSONObject

object TimerStore {
    class State(var mode: String, var total: Long) {
        var label: String = ""
        var spec: Long = total
        var running = false

        var base: Long = if (mode == "stopwatch") 0L else total
        var startedAt = 0L
        var doneAt = 0L

        val stopwatch get() = mode == "stopwatch"

        fun value(now: Long = SystemClock.elapsedRealtime()): Long {
            if (!running) return base
            val passed = now - startedAt
            return if (stopwatch) base + passed else (base - passed).coerceAtLeast(0L)
        }

        val touched get() = running || base != (if (stopwatch) 0L else total)
    }

    private val states = HashMap<String, State>()
    private val main = Handler(Looper.getMainLooper())
    var onDone: ((id: String, label: String) -> Unit)? = null
    var onChange: (() -> Unit)? = null

    fun get(id: String): State? = states[id]

    fun sync(layout: Layout) {
        val seen = HashSet<String>()
        for (p in layout.pages) for (t in p.tiles) {
            val spec = t.timer ?: continue
            seen.add(t.id)
            val ms = spec.secs * 1000L
            val s = states[t.id]
            if (s == null || s.mode != spec.mode || (s.spec != ms && !s.touched)) {
                states[t.id] = State(spec.mode, ms).also { it.spec = ms }
            } else s.spec = ms
            states[t.id]?.label = t.label ?: if (spec.mode == "stopwatch") "Kronometre" else I18n.t("Timer")
        }
        states.keys.retainAll(seen)
    }

    fun toggle(id: String) {
        val s = states[id] ?: return
        val now = SystemClock.elapsedRealtime()
        if (s.running) {
            s.base = s.value(now); s.running = false
        } else {
            if (!s.stopwatch && s.base <= 0L) s.base = s.total
            s.doneAt = 0L
            s.startedAt = now; s.running = true
            schedule(id, s)
        }
        onChange?.invoke()
    }

    fun reset(id: String) {
        val s = states[id] ?: return
        s.running = false; s.doneAt = 0L
        s.total = s.spec
        s.base = if (s.stopwatch) 0L else s.total
        onChange?.invoke()
    }

    fun addMinute(id: String) {
        val s = states[id] ?: return
        if (s.stopwatch) return
        val now = SystemClock.elapsedRealtime()
        val v = s.value(now)
        s.doneAt = 0L
        if (s.running) { s.base = v + 60_000L; s.startedAt = now; schedule(id, s) }
        else s.base = v + 60_000L
        if (s.base > s.total) s.total = s.base
        onChange?.invoke()
    }

    private fun schedule(id: String, s: State) {
        if (s.stopwatch) return
        val started = s.startedAt
        main.postDelayed({
            val cur = states[id] ?: return@postDelayed
            if (!cur.running || cur.startedAt != started) return@postDelayed
            if (cur.value() > 0L) { schedule(id, cur); return@postDelayed }
            cur.running = false; cur.base = 0L; cur.doneAt = SystemClock.uptimeMillis()
            onDone?.invoke(id, cur.label)
            onChange?.invoke()
        }, s.value().coerceAtLeast(10L))
    }
}

object PluginStore {
    class Value(val v: String, val sub: String?, val color: Int?, val pct: Float?)

    private val values = HashMap<String, Value>()
    var onChange: (() -> Unit)? = null

    fun get(id: String): Value? = values[id]

    fun apply(s: JSONObject) {
        val keys = s.keys()
        while (keys.hasNext()) {
            val id = keys.next()
            val o = s.optJSONObject(id) ?: continue
            val color = o.optString("c", "").takeIf { it.isNotEmpty() && it != "null" }?.let {
                try { android.graphics.Color.parseColor(it) } catch (_: IllegalArgumentException) { null }
            }
            val pct = if (o.isNull("p") || !o.has("p")) null else o.optDouble("p", 0.0).toFloat().coerceIn(0f, 1f)
            values[id] = Value(o.optString("v", ""), if (o.isNull("sub")) null else o.optString("sub").ifEmpty { null }, color, pct)
        }
        onChange?.invoke()
    }
}

object WeatherStore {
    class Now(val t: Int?, val feels: Int?, val hum: Int?, val wind: Int?, val cond: String, val icon: String, val hi: Int?, val lo: Int?)
    class Hour(val h: String, val t: Int?, val icon: String, val pc: Int?)
    class Day(val day: String, val hi: Int?, val lo: Int?, val icon: String, val cond: String, val rain: Int?)
    class Data(val place: String, val at: String, val now: Now, val hours: List<Hour>, val tomorrow: Day)

    var data: Data? = null
        private set
    var onChange: (() -> Unit)? = null

    private fun JSONObject.int(k: String): Int? = if (isNull(k) || !has(k)) null else optInt(k)

    fun parse(o: JSONObject): Data? = try {
        val n = o.getJSONObject("now")
        val now = Now(n.int("t"), n.int("feels"), n.int("hum"), n.int("wind"), n.optString("cond"), n.optString("icon", "cloud"), n.int("hi"), n.int("lo"))
        val ha = o.optJSONArray("hours")
        val hours = ArrayList<Hour>()
        if (ha != null) for (i in 0 until ha.length()) ha.optJSONObject(i)?.let {
            hours.add(Hour(it.optString("h"), it.int("t"), it.optString("icon", "cloud"), it.int("pc")))
        }
        val tm = o.optJSONObject("tomorrow") ?: JSONObject()
        val day = Day(tm.optString("day", I18n.t("Tomorrow")), tm.int("hi"), tm.int("lo"), tm.optString("icon", "cloud"), tm.optString("cond"), tm.int("rain"))
        Data(o.optString("place"), o.optString("at"), now, hours, day)
    } catch (_: Exception) { null }

    fun apply(d: Data?) {
        if (d == null) return
        data = d
        onChange?.invoke()
    }
}
