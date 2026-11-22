package app.mydeck

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import java.util.concurrent.Executors

object GifStore {
    private val decoder = Executors.newSingleThreadExecutor()
    private val ready = HashMap<String, Array<Bitmap>>()
    private val loading = HashSet<String>()
    var onReady: (() -> Unit)? = null

    fun frames(g: GifSpec): Array<Bitmap>? {
        ready[g.url]?.let { return it }
        if (!loading.add(g.url)) return null
        val name = g.url.substringAfterLast('/')
        AssetCache.get(g.url, name) { file ->
            if (file == null) { loading.remove(g.url); return@get }
            decoder.execute {
                val frames = try {
                    val o = BitmapFactory.Options().apply {
                        inPreferredConfig = if (g.fill == "bg") Bitmap.Config.RGB_565 else Bitmap.Config.ARGB_8888
                    }
                    val sheet = BitmapFactory.decodeFile(file.absolutePath, o)
                    val fh = sheet.height / g.frames.coerceAtLeast(1)
                    val list = Array(g.frames) { i -> Bitmap.createBitmap(sheet, 0, i * fh, sheet.width, fh) }
                    if (list.none { it === sheet }) sheet.recycle()
                    list
                } catch (_: Throwable) { null }
                android.os.Handler(android.os.Looper.getMainLooper()).post {
                    loading.remove(g.url)
                    if (frames != null) { ready[g.url] = frames; onReady?.invoke() }
                }
            }
        }
        return null
    }

    fun retain(urls: Set<String>) {
        val it = ready.entries.iterator()
        while (it.hasNext()) {
            val e = it.next()
            if (e.key !in urls) { e.value.forEach { b -> b.recycle() }; it.remove() }
        }
    }
}
