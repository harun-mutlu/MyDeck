package app.mydeck

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.MediaPlayer
import android.view.SurfaceHolder
import android.view.SurfaceView
import android.view.View
import java.io.File

class WallPlayer(context: Context, private val deck: DeckView) : SurfaceHolder.Callback {
    val surface = SurfaceView(context).apply { visibility = View.GONE }
    private var player: MediaPlayer? = null
    private var videoFile: File? = null
    private var surfaceReady = false
    private var current: String? = null
    private var paused = false

    init { surface.holder.addCallback(this) }

    fun show(w: Wall?) {
        current = w?.name
        if (w == null) { stopVideo(); deck.wallBitmap = null; deck.videoWall = false; return }
        val want = w.name
        AssetCache.get(w.url, w.name) { file ->
            if (current != want || file == null) return@get
            if (w.type == "video") {
                deck.wallBitmap = null
                videoFile = file
                surface.visibility = View.VISIBLE
                startVideo()
            } else {
                stopVideo()
                Thread {
                    val o = BitmapFactory.Options().apply { inPreferredConfig = Bitmap.Config.RGB_565 }
                    val b = try { BitmapFactory.decodeFile(file.absolutePath, o) } catch (_: Throwable) { null }
                    deck.post {
                        if (current != want) { b?.recycle(); return@post }
                        val old = deck.wallBitmap
                        deck.videoWall = false
                        deck.wallBitmap = b
                        if (old != null && old !== b) old.recycle()
                    }
                }.start()
            }
        }
    }

    private fun startVideo() {
        val f = videoFile ?: return
        if (!surfaceReady || paused) return
        player?.release()
        player = try {
            MediaPlayer().apply {
                setDataSource(f.absolutePath)
                setDisplay(surface.holder)
                isLooping = true
                setVolume(0f, 0f)
                setOnPreparedListener { it.start(); deck.videoWall = true }
                setOnErrorListener { _, _, _ -> deck.videoWall = false; true }
                prepareAsync()
            }
        } catch (_: Exception) { null }
    }

    private fun stopVideo() {
        player?.release(); player = null
        videoFile = null
        deck.videoWall = false
        surface.visibility = View.GONE
    }

    fun pause() { paused = true; player?.release(); player = null; deck.videoWall = false }
    fun resume() { paused = false; if (videoFile != null) startVideo() }

    override fun surfaceCreated(holder: SurfaceHolder) { surfaceReady = true; startVideo() }
    override fun surfaceChanged(holder: SurfaceHolder, format: Int, width: Int, height: Int) {}
    override fun surfaceDestroyed(holder: SurfaceHolder) { surfaceReady = false; player?.release(); player = null; deck.videoWall = false }
}
