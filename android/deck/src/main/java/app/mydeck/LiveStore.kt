package app.mydeck

import android.os.SystemClock
import org.json.JSONObject

object LiveStore {
    const val CAP = 301

    private val hist = HashMap<String, FloatArray>()
    private var head = 0
    private var count = 0
    val latest = HashMap<String, Float>()
    var procs: List<Pair<String, Float>> = emptyList()
        private set

    var tick = 0L
        private set
    private var lastAt = 0L

    fun phase(): Float = if (lastAt == 0L) 1f else ((SystemClock.uptimeMillis() - lastAt) / 1000f).coerceIn(0f, 1f)

    fun get(key: String, back: Int): Float {
        val a = hist[key] ?: return 0f
        if (count == 0) return 0f
        val b = if (back >= count) count - 1 else back
        return a[(head - 1 - b + CAP * 2) % CAP]
    }

    class Sample(val values: Map<String, Float>, val procs: List<Pair<String, Float>>?)
    class History(val values: Map<String, FloatArray>, val n: Int)

    fun parseSample(o: JSONObject): Sample {
        val v = HashMap<String, Float>()
        o.optJSONObject("v")?.let { j -> j.keys().forEach { k -> v[k] = j.optDouble(k, 0.0).toFloat() } }
        val p = o.optJSONArray("p")?.let { arr ->
            (0 until arr.length()).mapNotNull { i ->
                arr.optJSONArray(i)?.let { it.optString(0) to it.optDouble(1, 0.0).toFloat() }
            }
        }
        return Sample(v, p)
    }

    fun parseHistory(o: JSONObject): History {
        val map = HashMap<String, FloatArray>()
        var n = 0
        o.optJSONObject("h")?.let { j ->
            j.keys().forEach { k ->
                val arr = j.optJSONArray(k) ?: return@forEach
                val f = FloatArray(arr.length()) { arr.optDouble(it, 0.0).toFloat() }
                n = maxOf(n, f.size)
                map[k] = f
            }
        }
        return History(map, minOf(n, CAP))
    }

    fun apply(s: Sample) {
        for ((k, value) in s.values) {
            val a = hist.getOrPut(k) { FloatArray(CAP).also { arr -> if (count > 0) arr.fill(value) } }
            a[head] = value
            latest[k] = value
        }
        head = (head + 1) % CAP
        if (count < CAP) count++
        tick++
        s.procs?.let { procs = it }
        lastAt = SystemClock.uptimeMillis()
    }

    fun apply(h: History) {
        if (h.n == 0) return
        hist.clear()
        for ((k, f) in h.values) {
            val a = FloatArray(CAP)
            val off = f.size - h.n
            for (i in 0 until h.n) a[i] = f[off + i]
            hist[k] = a
            latest[k] = f.last()
        }
        head = h.n % CAP
        count = h.n
        lastAt = SystemClock.uptimeMillis()
    }
}
