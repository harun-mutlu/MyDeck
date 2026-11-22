package app.mydeck

import android.content.Context
import android.os.Handler
import android.os.Looper
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.util.concurrent.Executors

object AssetCache {
    private val io = Executors.newFixedThreadPool(2)
    private val main = Handler(Looper.getMainLooper())
    private val waiting = HashMap<String, MutableList<(File?) -> Unit>>()
    private lateinit var dir: File

    fun init(ctx: Context) {
        if (::dir.isInitialized) return
        dir = File(ctx.cacheDir, "assets").apply { mkdirs() }
    }

    fun get(url: String, name: String, done: (File?) -> Unit) {
        val f = File(dir, name)
        if (f.exists() && f.length() > 0) { f.setLastModified(System.currentTimeMillis()); done(f); return }
        synchronized(waiting) {
            waiting[name]?.let { it.add(done); return }
            waiting[name] = mutableListOf(done)
        }
        io.execute {
            var ok: File? = null
            try {
                val tmp = File(dir, "$name.part")
                val conn = URL(url).openConnection() as HttpURLConnection
                conn.connectTimeout = 5000
                conn.readTimeout = 20000
                conn.inputStream.use { input -> tmp.outputStream().use { input.copyTo(it, 64 * 1024) } }
                conn.disconnect()
                if (tmp.length() > 0 && tmp.renameTo(f)) ok = f
                trim()
            } catch (_: Exception) { }
            val cbs = synchronized(waiting) { waiting.remove(name) } ?: return@execute
            main.post { cbs.forEach { it(ok) } }
        }
    }

    private fun trim() {
        val files = dir.listFiles()?.filter { !it.name.endsWith(".part") }?.sortedBy { it.lastModified() } ?: return
        var total = files.sumOf { it.length() }
        for (f in files) {
            if (total < 150L * 1024 * 1024) break
            total -= f.length()
            f.delete()
        }
    }
}
