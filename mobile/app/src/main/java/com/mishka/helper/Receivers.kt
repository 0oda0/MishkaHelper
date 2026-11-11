package com.mishka.helper

import android.app.Activity
import android.app.Notification
import android.app.RemoteInput
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.service.notification.NotificationListenerService
import android.service.notification.StatusBarNotification
import android.widget.Toast
import org.json.JSONArray
import org.json.JSONObject

/**
 * Phone notifications → PC toasts. Ongoing ones (music, downloads, navigation) and group summaries are skipped,
 * except a ringing call. Their buttons go along, but only two kinds: a call's (answer / decline) and replies with text.
 * Anything else ("подтвердить платёж", "удалить") is never offered to the PC.
 */
class NotifListener : NotificationListenerService() {
    companion object {
        private val actions = object : LinkedHashMap<String, List<Notification.Action>>() {
            override fun removeEldestEntry(e: MutableMap.MutableEntry<String, List<Notification.Action>>?) = size > 30
        }

        /** The PC pressed button i of notification id: only buttons we listed ourselves can be pressed. */
        fun act(c: Context, id: String, i: Int, text: String?) {
            val a = synchronized(actions) { actions[id] }?.getOrNull(i) ?: return
            val fill = Intent()
            val inputs = a.remoteInputs
            if (!inputs.isNullOrEmpty()) {
                if (text.isNullOrBlank()) return
                val b = Bundle(); inputs.forEach { b.putCharSequence(it.resultKey, text) }
                RemoteInput.addResultsToIntent(inputs, fill, b)
            }
            runCatching { a.actionIntent.send(c, 0, fill) }
        }
    }

    private var lastKey = ""

    override fun onNotificationPosted(sbn: StatusBarNotification) {
        val n = sbn.notification
        // A ringing call is ongoing and has a full-screen intent; the call in progress (with its ticking timer) has none.
        val call = n.category == Notification.CATEGORY_CALL && n.fullScreenIntent != null
        if (sbn.packageName == packageName || (sbn.isOngoing && !call)) return
        if (n.flags and Notification.FLAG_GROUP_SUMMARY != 0) return
        // Apps mark codes and banking alerts "secret" (hidden even on the lock screen) — they don't travel over Wi-Fi.
        if (n.visibility == Notification.VISIBILITY_SECRET) return
        if (n.category == Notification.CATEGORY_PROGRESS || n.category == Notification.CATEGORY_TRANSPORT) return
        val title = n.extras.getCharSequence(Notification.EXTRA_TITLE)?.toString() ?: return
        val text = (n.extras.getCharSequence(Notification.EXTRA_BIG_TEXT) ?: n.extras.getCharSequence(Notification.EXTRA_TEXT))?.toString() ?: ""
        val key = "${sbn.packageName}|$title|$text"
        if (key == lastKey) return   // apps re-post the same notification on every update
        lastKey = key
        val app = runCatching { packageManager.getApplicationLabel(packageManager.getApplicationInfo(sbn.packageName, 0)).toString() }.getOrDefault(sbn.packageName)
        val acts = (n.actions ?: emptyArray()).filter { call || !it.remoteInputs.isNullOrEmpty() }.take(3)
        synchronized(actions) { actions[sbn.key] = acts }
        val arr = JSONArray()
        acts.forEach { arr.put(JSONObject().put("title", it.title?.toString() ?: "").put("reply", !it.remoteInputs.isNullOrEmpty())) }
        LinkService.send(JSONObject().put("t", "notif").put("app", app).put("title", title).put("text", text.take(500))
            .put("id", sbn.key).put("call", call).put("actions", arr))
    }
}

/** After a reboot the link comes back by itself (if the phone was paired). */
class BootReceiver : BroadcastReceiver() {
    override fun onReceive(c: Context, i: Intent) {
        if (i.action == Intent.ACTION_BOOT_COMPLETED) LinkService.start(c)
    }
}

/** "Поделиться → На ПК (Mishka)": files are uploaded to Загрузки\С телефона on the PC, text goes to the PC clipboard. */
class ShareActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (!Link.paired(this)) { Toast.makeText(this, "Сначала подключи ПК в приложении Mishka", Toast.LENGTH_LONG).show(); finish(); return }
        val uris = mutableListOf<Uri>()
        @Suppress("DEPRECATION")
        when (intent.action) {
            Intent.ACTION_SEND -> (if (Build.VERSION.SDK_INT >= 33) intent.getParcelableExtra(Intent.EXTRA_STREAM, Uri::class.java) else intent.getParcelableExtra(Intent.EXTRA_STREAM))?.let { uris += it }
            Intent.ACTION_SEND_MULTIPLE -> (if (Build.VERSION.SDK_INT >= 33) intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM, Uri::class.java) else intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM))?.let { uris += it }
        }
        val text = intent.getStringExtra(Intent.EXTRA_TEXT)
        Toast.makeText(this, if (uris.isNotEmpty()) "Отправляю на ПК: ${uris.size}…" else "Отправляю текст на ПК…", Toast.LENGTH_SHORT).show()
        val app = applicationContext
        Thread {
            val ok = if (uris.isNotEmpty()) uris.count { runCatching { Link.upload(app, it) }.getOrDefault(false) } == uris.size
                     else text != null && (LinkService.send(JSONObject().put("t", "clip").put("text", text)) || runCatching { Link.call(app, "clipSet", text) != null }.getOrDefault(false))
            runOnUiThread { Toast.makeText(app, if (ok) "Готово — на ПК" else "Не получилось: ПК не в сети?", Toast.LENGTH_LONG).show() }
        }.start()
        finish()
    }
}
