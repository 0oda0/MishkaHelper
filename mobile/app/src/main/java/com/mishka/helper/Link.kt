package com.mishka.helper

import android.content.Context
import android.net.Uri
import android.provider.OpenableColumns
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import okio.BufferedSink
import okio.source
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.net.NetworkInterface
import java.net.SocketTimeoutException
import java.net.URLEncoder
import java.util.concurrent.TimeUnit

/** A Mishka Helper PC found on the network. */
data class Pc(val name: String, val host: String, val port: Int, val version: String)

/**
 * Talking to the PC: discovery (UDP broadcast "MISHKA?" → "MISHKA|name|port|version"),
 * the saved pairing (host, port, 6-digit PIN), and plain HTTP calls with the PIN header.
 */
object Link {
    const val DISCOVERY_PORT = 8771

    val http: OkHttpClient = OkHttpClient.Builder()
        .connectTimeout(5, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .pingInterval(20, TimeUnit.SECONDS)   // keeps the WebSocket alive through Wi-Fi power saving
        .build()

    private fun prefs(c: Context) = c.getSharedPreferences("link", Context.MODE_PRIVATE)

    fun host(c: Context) = prefs(c).getString("host", "") ?: ""
    fun port(c: Context) = prefs(c).getInt("port", 8770)
    fun pin(c: Context) = prefs(c).getString("pin", "") ?: ""
    fun pcName(c: Context) = prefs(c).getString("name", "") ?: ""
    fun paired(c: Context) = host(c).isNotEmpty() && pin(c).length >= 6
    fun base(c: Context) = "http://${host(c)}:${port(c)}"

    fun save(c: Context, pc: Pc, pin: String) =
        prefs(c).edit().putString("host", pc.host).putInt("port", pc.port).putString("pin", pin).putString("name", pc.name).apply()

    fun forget(c: Context) = prefs(c).edit().clear().apply()

    /** Broadcasts on every Wi-Fi/Ethernet interface and collects answers for ~1.5 s. Call off the main thread. */
    fun discover(): List<Pc> {
        val found = linkedMapOf<String, Pc>()
        DatagramSocket().use { s ->
            s.broadcast = true
            s.soTimeout = 300
            val ask = "MISHKA?".toByteArray()
            val targets = mutableSetOf<InetAddress>(InetAddress.getByName("255.255.255.255"))
            NetworkInterface.getNetworkInterfaces()?.toList().orEmpty().filter { it.isUp && !it.isLoopback }.forEach { ni ->
                ni.interfaceAddresses.mapNotNull { it.broadcast }.forEach { targets += it }
            }
            repeat(3) {
                targets.forEach { runCatching { s.send(DatagramPacket(ask, ask.size, it, DISCOVERY_PORT)) } }
                val until = System.currentTimeMillis() + 500
                while (System.currentTimeMillis() < until) {
                    val buf = ByteArray(512)
                    val p = DatagramPacket(buf, buf.size)
                    try { s.receive(p) } catch (_: SocketTimeoutException) { continue }
                    val parts = String(p.data, 0, p.length).split("|")
                    if (parts.size >= 3 && parts[0] == "MISHKA") {
                        val host = p.address.hostAddress ?: continue
                        found[host] = Pc(parts[1], host, parts[2].toIntOrNull() ?: 8770, parts.getOrElse(3) { "" })
                    }
                }
            }
        }
        return found.values.toList()
    }

    /** POST /api/{cmd} with the PIN. Returns the JSON answer, or null (wrong PIN → "401"). */
    fun call(c: Context, cmd: String, arg: String = "", host: String = host(c), port: Int = port(c), pin: String = pin(c)): JSONObject? {
        val req = Request.Builder().url("http://$host:$port/api/$cmd").header("X-Pin", pin)
            .post(arg.toRequestBody("text/plain; charset=utf-8".toMediaType())).build()
        http.newCall(req).execute().use { r ->
            if (r.code == 401) return JSONObject().put("error", "pin")
            if (r.code == 429) return JSONObject().put("error", "locked")
            if (!r.isSuccessful) return null
            return runCatching { JSONObject(r.body?.string() ?: "{}") }.getOrNull()
        }
    }

    /** Phone → PC file: streamed, so a 2 GB video doesn't need 2 GB of RAM. */
    fun upload(c: Context, uri: Uri): Boolean {
        val cr = c.contentResolver
        var name = "file"
        var size = -1L
        cr.query(uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null)?.use {
            if (it.moveToFirst()) { name = it.getString(0) ?: name; size = if (it.isNull(1)) -1 else it.getLong(1) }
        }
        val body = object : RequestBody() {
            override fun contentType() = "application/octet-stream".toMediaType()
            override fun contentLength() = size
            override fun writeTo(sink: BufferedSink) { cr.openInputStream(uri)!!.source().use { sink.writeAll(it) } }
        }
        val req = Request.Builder().url("${base(c)}/api/upload?name=" + URLEncoder.encode(name, "UTF-8"))
            .header("X-Pin", pin(c)).post(body).build()
        return http.newBuilder().readTimeout(10, TimeUnit.MINUTES).build().newCall(req).execute().use { it.isSuccessful }
    }
}
