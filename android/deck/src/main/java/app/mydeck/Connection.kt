package app.mydeck

import android.content.Context
import android.net.wifi.WifiManager
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.concurrent.Executors

class Connection(
    private val context: Context,
    private val hello: () -> JSONObject,
    private val listener: Listener,
) {
    interface Listener {
        fun onStatus(state: State, detail: String)
        fun onPairCode(code: String)
        fun onLayout(layout: Layout)
        fun onToast(text: String)
        fun onState(states: JSONObject)
        fun onUpdate(url: String) {}

        fun onKey(key: String) {}

        fun onLayoutJson(json: String) {}

        fun onScreen(pcIdle: Boolean) {}

        fun onGoto(pageId: String?, back: Boolean) {}
    }

    enum class State { SEARCHING, CONNECTING, PAIRING, CONNECTED, DENIED }

    companion object {
        const val PORT = 47801
        const val DISCOVERY = 47800
    }

    private val main = Handler(Looper.getMainLooper())
    private val writer = Executors.newSingleThreadExecutor()
    private val prefs = context.getSharedPreferences("mydeck", Context.MODE_PRIVATE)

    @Volatile private var running = false
    @Volatile private var token = Any()
    @Volatile private var socket: Socket? = null
    @Volatile private var out: OutputStreamWriter? = null
    private var thread: Thread? = null

    fun start() {
        if (running) return
        running = true
        val my = Any()
        token = my
        thread = Thread({ loop(my) }, "mydeck-net").apply { isDaemon = true; start() }
    }

    fun stop() {
        running = false
        try { socket?.close() } catch (_: Exception) {}
        thread?.interrupt()
    }

    fun send(msg: JSONObject) {
        val line = msg.toString() + "\n"
        writer.execute {
            try { out?.let { it.write(line); it.flush() } } catch (_: Exception) { closeQuietly() }
        }
    }

    private fun post(block: () -> Unit) = main.post { if (running) block() }

    private fun alive(my: Any) = running && my === token

    private fun loop(my: Any) {
        var backoff = 500L
        while (alive(my)) {
            post { listener.onStatus(State.SEARCHING, I18n.t("Looking for your computer…")) }
            val target = findServer()
            if (target == null) {
                sleep(backoff); backoff = (backoff * 2).coerceAtMost(4000); continue
            }
            post { listener.onStatus(State.CONNECTING, I18n.t("Connecting · {0}", target.third)) }
            if (!alive(my)) break
            val denied = session(target.first, target.second, target.third, my)
            if (denied) {
                post { listener.onStatus(State.DENIED, I18n.t("The computer declined the pairing")) }
                sleep(10_000)
            } else sleep(400)
            backoff = 500L
        }
    }

    private var attempts = 0

    fun setManualHost(host: String?) {
        val h = host?.trim()?.ifEmpty { null }
        prefs.edit().putString("manual", h).apply()
        if (h != null) prefs.edit().putString("host", h).apply()
        closeQuietly()
        thread?.interrupt()
    }

    fun manualHost(): String? = prefs.getString("manual", null)

    private fun reachable(host: String, timeout: Int): Boolean = try {
        Socket().use { s -> s.connect(InetSocketAddress(host, PORT), timeout) }
        true
    } catch (_: Exception) { false }

    private fun findServer(): Triple<InetAddress, Int, String>? {
        val name = prefs.getString("hostName", "PC") ?: "PC"
        for (h in listOfNotNull(prefs.getString("manual", null), prefs.getString("host", null)).distinct()) {
            if (reachable(h, 700)) return Triple(InetAddress.getByName(h), PORT, name)
        }
        broadcastSearch()?.let { return it }
        attempts++
        if (attempts % 3 == 1) scanSubnet()?.let { return it }
        return null
    }

    private fun wifiInfo(): Pair<Int, Int>? {
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        @Suppress("DEPRECATION")
        val dhcp = wifi.dhcpInfo ?: return null
        if (dhcp.ipAddress == 0) return null
        val mask = if (dhcp.netmask == 0) 0x00FFFFFF else dhcp.netmask
        return dhcp.ipAddress to mask
    }

    private fun toAddress(le: Int): InetAddress = InetAddress.getByAddress(
        byteArrayOf((le and 0xFF).toByte(), (le shr 8 and 0xFF).toByte(), (le shr 16 and 0xFF).toByte(), (le shr 24 and 0xFF).toByte()))

    private fun broadcastSearch(): Triple<InetAddress, Int, String>? {
        val wifi = context.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
        val lock = wifi.createMulticastLock("mydeck").apply { setReferenceCounted(false) }
        val targets = mutableListOf(InetAddress.getByName("255.255.255.255"))
        wifiInfo()?.let { (ip, mask) -> targets.add(toAddress((ip and mask) or mask.inv())) }
        var sock: DatagramSocket? = null
        return try {
            lock.acquire()
            sock = try {
                DatagramSocket(null as java.net.SocketAddress?).apply { reuseAddress = true; bind(InetSocketAddress(DISCOVERY)) }
            } catch (_: Exception) { DatagramSocket() }
            sock.broadcast = true
            sock.soTimeout = 400
            val msg = "MYDECK?".toByteArray()
            val buf = ByteArray(256)
            val until = SystemClock.uptimeMillis() + 2400
            var nextSend = 0L
            while (SystemClock.uptimeMillis() < until) {
                if (SystemClock.uptimeMillis() >= nextSend) {
                    for (t in targets) try { sock.send(DatagramPacket(msg, msg.size, t, DISCOVERY)) } catch (_: Exception) {}
                    nextSend = SystemClock.uptimeMillis() + 800
                }
                try {
                    val p = DatagramPacket(buf, buf.size)
                    sock.receive(p)
                    val parts = String(p.data, 0, p.length, Charsets.UTF_8).trim().split(" ", limit = 3)
                    if (parts.size >= 2 && parts[0] == "MYDECK!") {
                        val name = parts.getOrElse(2) { "PC" }
                        prefs.edit().putString("host", p.address.hostAddress).putString("hostName", name).apply()
                        return Triple(p.address, parts[1].toIntOrNull() ?: PORT, name)
                    }
                } catch (_: SocketTimeoutException) {}
            }
            null
        } catch (_: Exception) {
            null
        } finally {
            try { sock?.close() } catch (_: Exception) {}
            try { lock.release() } catch (_: Exception) {}
        }
    }

    private fun scanSubnet(): Triple<InetAddress, Int, String>? {
        val (ip, mask0) = wifiInfo() ?: return null
        val hostBits = Integer.bitCount(mask0.inv())
        val mask = if (hostBits > 10) 0x00FFFFFF else mask0
        val base = Integer.reverseBytes(ip and mask)
        val count = Integer.reverseBytes(mask.inv()).let { if (it <= 0) 255 else it }
        val self = Integer.reverseBytes(ip)
        val found = java.util.concurrent.atomic.AtomicReference<String?>(null)
        val pool = Executors.newFixedThreadPool(48)
        try {
            for (i in 1 until count) {
                val be = base + i
                if (be == self) continue
                pool.execute {
                    if (found.get() != null) return@execute
                    val host = "${be ushr 24 and 0xFF}.${be ushr 16 and 0xFF}.${be ushr 8 and 0xFF}.${be and 0xFF}"
                    if (reachable(host, 300)) found.compareAndSet(null, host)
                }
            }
            pool.shutdown()
            pool.awaitTermination(15, java.util.concurrent.TimeUnit.SECONDS)
        } catch (_: Exception) {
        } finally { pool.shutdownNow() }
        val host = found.get() ?: return null
        prefs.edit().putString("host", host).apply()
        return Triple(InetAddress.getByName(host), PORT, prefs.getString("hostName", "PC") ?: "PC")
    }

    private fun session(host: InetAddress, port: Int, name: String, my: Any): Boolean {
        var denied = false
        val s = Socket()
        try {
            s.tcpNoDelay = true
            s.connect(InetSocketAddress(host, port), 1500)
            s.soTimeout = 7000
            socket = s
            val w = OutputStreamWriter(s.getOutputStream(), Charsets.UTF_8)
            out = w
            val r = BufferedReader(InputStreamReader(s.getInputStream(), Charsets.UTF_8), 64 * 1024)
            send(hello())

            val pinger = Thread({
                var n = 0
                while (alive(my) && !s.isClosed) {
                    send(JSONObject().put("t", "ping").put("n", n++))
                    try { Thread.sleep(2000) } catch (_: InterruptedException) { break }
                }
            }, "mydeck-ping").apply { isDaemon = true; start() }

            while (alive(my)) {
                val line = r.readLine() ?: break
                val msg = try { JSONObject(line) } catch (_: Exception) { continue }

                try { when (msg.optString("t")) {
                    "pair" -> {
                        val code = msg.optString("code")
                        post { listener.onStatus(State.PAIRING, name); listener.onPairCode(code) }
                    }
                    "key" -> {
                        val k = msg.optString("k")
                        if (k.isNotEmpty()) listener.onKey(k)
                    }
                    "layout" -> {
                        msg.optString("lang").takeIf { it == "tr" || it == "en" }?.let { I18n.lang = it }
                        val layout = Layout.parse(msg)
                        listener.onLayoutJson(line)
                        post { listener.onStatus(State.CONNECTED, name); listener.onLayout(layout) }
                    }
                    "state" -> {
                        val st = msg.optJSONObject("s")
                        if (st != null) post { listener.onState(st) }
                    }
                    "toast" -> {
                        val text = msg.optString("text")
                        post { listener.onToast(text) }
                    }
                    "screen" -> {
                        val idle = msg.optBoolean("idle")
                        post { listener.onScreen(idle) }
                    }
                    "sys" -> {
                        val sample = LiveStore.parseSample(msg)
                        post { LiveStore.apply(sample) }
                    }
                    "media" -> {
                        val m = MediaStore.parse(msg)
                        post { MediaStore.apply(m) }
                    }
                    "pv" -> {
                        val s = msg.optJSONObject("s")
                        if (s != null) post { PluginStore.apply(s) }
                    }
                    "wx" -> {
                        val w = WeatherStore.parse(msg)
                        post { WeatherStore.apply(w) }
                    }
                    "sysh" -> {
                        val h = LiveStore.parseHistory(msg)
                        post { LiveStore.apply(h) }
                    }
                    "update" -> {
                        val url = msg.optString("url")
                        if (url.isNotEmpty()) post { listener.onUpdate(url) }
                    }
                    "goto" -> {
                        val id = if (msg.isNull("page")) null else msg.optString("page").ifEmpty { null }
                        val back = msg.optBoolean("back", false)
                        post { listener.onGoto(id, back) }
                    }
                    "denied" -> { denied = true; break }
                } } catch (e: java.io.IOException) { throw e } catch (_: Exception) { }
            }
            pinger.interrupt()
        } catch (_: Exception) {
        } finally {
            out = null
            socket = null
            try { s.close() } catch (_: Exception) {}
        }
        return denied
    }

    private fun closeQuietly() {
        try { socket?.close() } catch (_: Exception) {}
    }

    private fun sleep(ms: Long) {
        try { Thread.sleep(ms) } catch (_: InterruptedException) {}
    }
}
