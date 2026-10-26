package com.mishka.helper

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.BroadcastReceiver
import android.content.ClipData
import android.content.ClipboardManager
import android.content.ContentValues
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.media.AudioAttributes
import android.media.AudioManager
import android.media.MediaPlayer
import android.media.RingtoneManager
import android.os.BatteryManager
import android.os.Build
import android.os.Environment
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.StatFs
import android.os.VibrationEffect
import android.os.Vibrator
import android.provider.MediaStore
import kotlinx.coroutines.flow.MutableStateFlow
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject

/**
 * Keeps the phone linked to the PC: one WebSocket (reconnects by itself), status every minute,
 * and executes what the PC asks: ring ("найти телефон"), clipboard, receive a file.
 * Runs as a foreground service so Android doesn't cut the connection.
 */
class LinkService : Service() {
    companion object {
        val connected = MutableStateFlow(false)
        val lastError = MutableStateFlow("")
        @Volatile private var ws: WebSocket? = null

        /** Phone → PC message; false when not connected. */
        fun send(o: JSONObject): Boolean = ws?.send(o.toString()) ?: false

        fun start(c: Context) {
            if (Link.paired(c)) c.startForegroundService(Intent(c, LinkService::class.java))
        }
        fun stop(c: Context) = c.stopService(Intent(c, LinkService::class.java))

        const val CH_LINK = "link"
        const val CH_EVENTS = "events"
        const val ACTION_STOP_RING = "stopRing"
    }

    private val main = Handler(Looper.getMainLooper())
    private var stopped = false
    private var retry = 1000L
    private var player: MediaPlayer? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        val nm = getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(NotificationChannel(CH_LINK, "Связь с ПК", NotificationManager.IMPORTANCE_MIN))
        nm.createNotificationChannel(NotificationChannel(CH_EVENTS, "Файлы и поиск телефона", NotificationManager.IMPORTANCE_HIGH))
        val n = Notification.Builder(this, CH_LINK)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle("Mishka: связь с ${Link.pcName(this).ifEmpty { "ПК" }}")
            .setContentIntent(PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE))
            .build()
        if (Build.VERSION.SDK_INT >= 29) startForeground(1, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE) else startForeground(1, n)
        registerReceiver(battery, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        connect()
        main.postDelayed(tick, 60_000)
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == ACTION_STOP_RING) stopRing()
        return START_STICKY
    }

    override fun onDestroy() {
        stopped = true
        main.removeCallbacksAndMessages(null)
        runCatching { unregisterReceiver(battery) }
        ws?.close(1000, "bye"); ws = null
        connected.value = false
        stopRing()
        super.onDestroy()
    }

    private val tick = object : Runnable {
        override fun run() { sendStatus(); main.postDelayed(this, 60_000) }
    }

    private var lastBattery = -1
    private val battery = object : BroadcastReceiver() {
        override fun onReceive(c: Context, i: Intent) {
            val pct = i.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) * 100 / i.getIntExtra(BatteryManager.EXTRA_SCALE, 100).coerceAtLeast(1)
            if (pct != lastBattery) { lastBattery = pct; sendStatus() }
        }
    }

    private fun sendStatus() {
        val bi = registerReceiver(null, IntentFilter(Intent.ACTION_BATTERY_CHANGED))
        val level = bi?.getIntExtra(BatteryManager.EXTRA_LEVEL, -1) ?: -1
        val scale = bi?.getIntExtra(BatteryManager.EXTRA_SCALE, 100) ?: 100
        val plugged = (bi?.getIntExtra(BatteryManager.EXTRA_PLUGGED, 0) ?: 0) != 0
        val st = StatFs(Environment.getDataDirectory().path)
        send(JSONObject().put("t", "status").put("name", Build.MODEL).put("battery", level * 100 / scale.coerceAtLeast(1))
            .put("charging", plugged).put("free", st.availableBytes).put("total", st.totalBytes))
    }

    private fun connect() {
        if (stopped || !Link.paired(this)) return
        val req = Request.Builder().url("ws://${Link.host(this)}:${Link.port(this)}/api/ws").header("X-Pin", Link.pin(this)).build()
        ws = Link.http.newWebSocket(req, object : WebSocketListener() {
            override fun onOpen(webSocket: WebSocket, response: Response) {
                retry = 1000; connected.value = true; lastError.value = ""
                sendStatus()
            }
            override fun onMessage(webSocket: WebSocket, text: String) {
                val m = runCatching { JSONObject(text) }.getOrNull() ?: return
                main.post { handle(m) }
            }
            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = lost(reason)
            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) =
                lost(if (response?.code == 401) "Неверный PIN — подключись заново" else t.message ?: "нет связи")
        })
    }

    /** PC asleep, Wi-Fi switched, Mishka restarted: try again, waiting up to a minute between attempts. */
    private fun lost(why: String) {
        connected.value = false
        lastError.value = why
        if (stopped) return
        main.postDelayed({ connect() }, retry)
        retry = (retry * 2).coerceAtMost(60_000)
    }

    private fun handle(m: JSONObject) {
        when (m.optString("t")) {
            "ring" -> ring()
            "ringStop" -> stopRing()
            "clip" -> {
                getSystemService(ClipboardManager::class.java).setPrimaryClip(ClipData.newPlainText("Mishka", m.optString("text")))
                event("Текст с ПК в буфере", m.optString("text").take(80))
            }
            "toast" -> event("Mishka", m.optString("text"))
            "file" -> Thread { download(m.optString("id"), m.optString("name")) }.start()
        }
    }

    private fun event(title: String, text: String, intent: Intent? = null) {
        val n = Notification.Builder(this, CH_EVENTS).setSmallIcon(android.R.drawable.stat_notify_more)
            .setContentTitle(title).setContentText(text).setAutoCancel(true)
        if (intent != null) n.setContentIntent(PendingIntent.getActivity(this, 2, intent, PendingIntent.FLAG_IMMUTABLE))
        getSystemService(NotificationManager::class.java).notify((System.currentTimeMillis() % 100000).toInt() + 10, n.build())
    }

    /** PC → phone file: saved to Downloads/Mishka (no storage permission needed for that). */
    private fun download(id: String, name: String) {
        val req = Request.Builder().url("${Link.base(this)}/api/phone/file/$id").header("X-Pin", Link.pin(this)).build()
        runCatching {
            Link.http.newBuilder().readTimeout(10, java.util.concurrent.TimeUnit.MINUTES).build().newCall(req).execute().use { r ->
                if (!r.isSuccessful) error("HTTP ${r.code}")
                val values = ContentValues().apply {
                    put(MediaStore.Downloads.DISPLAY_NAME, name)
                    put(MediaStore.Downloads.RELATIVE_PATH, Environment.DIRECTORY_DOWNLOADS + "/Mishka")
                }
                val uri = contentResolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values) ?: error("нет места")
                contentResolver.openOutputStream(uri)!!.use { out -> r.body!!.byteStream().copyTo(out) }
                val open = Intent(Intent.ACTION_VIEW).setDataAndType(uri, r.header("Content-Type") ?: "*/*").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                main.post { event("Файл с ПК: $name", "Загрузки/Mishka · нажми, чтобы открыть", open) }
            }
        }.onFailure { main.post { event("Файл не принят", "$name: ${it.message}") } }
    }

    // ---------- "найти телефон" ----------
    private fun ring() {
        stopRing()
        val am = getSystemService(AudioManager::class.java)
        am.setStreamVolume(AudioManager.STREAM_ALARM, am.getStreamMaxVolume(AudioManager.STREAM_ALARM), 0)
        player = MediaPlayer().apply {
            setAudioAttributes(AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_ALARM).setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build())
            setDataSource(this@LinkService, RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM) ?: RingtoneManager.getDefaultUri(RingtoneManager.TYPE_RINGTONE))
            isLooping = true
            prepare(); start()
        }
        getSystemService(Vibrator::class.java)?.vibrate(VibrationEffect.createWaveform(longArrayOf(0, 600, 400), 0))
        val stop = PendingIntent.getService(this, 3, Intent(this, LinkService::class.java).setAction(ACTION_STOP_RING), PendingIntent.FLAG_IMMUTABLE)
        val n = Notification.Builder(this, CH_EVENTS).setSmallIcon(android.R.drawable.ic_lock_idle_alarm)
            .setContentTitle("Тебя ищут с ПК").setContentText("Нажми, чтобы выключить звук")
            .setContentIntent(stop).addAction(Notification.Action.Builder(null, "Нашёлся!", stop).build()).setOngoing(true).build()
        getSystemService(NotificationManager::class.java).notify(5, n)
        main.postDelayed({ stopRing() }, 60_000)
    }

    private fun stopRing() {
        player?.runCatching { stop(); release() }
        player = null
        getSystemService(Vibrator::class.java)?.cancel()
        getSystemService(NotificationManager::class.java)?.cancel(5)
    }
}
