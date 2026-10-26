package com.mishka.helper

import android.app.ActivityManager
import android.content.Context
import android.os.Environment
import java.io.File
import java.io.RandomAccessFile
import java.security.MessageDigest

/** One thing that can be deleted. `group` ties duplicates together (the oldest copy is kept, never selected). */
data class Junk(val file: File, val size: Long, val kind: Kind, val why: String, val group: String = "") {
    enum class Kind(val title: String, val safe: Boolean) {
        Apk("Установщики APK", true),
        Thumbs("Кэш миниатюр", true),
        Empty("Пустые папки", true),
        Big("Большие файлы (от 100 МБ)", false),
        Dupes("Одинаковые файлы (копии)", false),
        OldDownloads("Старые загрузки (больше 3 месяцев)", false),
    }
}

/**
 * Honest phone cleanup. Android doesn't let an app clear other apps' caches or "free RAM" — those buttons in
 * cleaner apps are placebo. What does free space: forgotten APKs, thumbnail caches, copies, huge old files.
 */
object Cleaner {
    private val root get() = Environment.getExternalStorageDirectory()

    fun hasAccess() = Environment.isExternalStorageManager()

    fun scan(progress: (String) -> Unit): List<Junk> {
        val out = mutableListOf<Junk>()
        val all = mutableListOf<File>()
        val empties = mutableListOf<File>()
        progress("Смотрю файлы…")
        fun walk(dir: File, depth: Int) {
            val kids = dir.listFiles() ?: return
            if (kids.isEmpty() && depth > 1) { empties += dir; return }
            for (f in kids) {
                if (f.isDirectory) {
                    // Android/data and Android/obb belong to apps (and are closed to us anyway).
                    if (depth == 0 && f.name == "Android") continue
                    if (f.name == ".thumbnails") { out += dirJunk(f, Junk.Kind.Thumbs, "миниатюры фото, пересоздаются сами"); continue }
                    walk(f, depth + 1)
                } else all += f
            }
        }
        walk(root, 0)

        val now = System.currentTimeMillis()
        val downloads = File(root, Environment.DIRECTORY_DOWNLOADS).absolutePath
        for (f in all) {
            val size = f.length()
            when {
                f.name.endsWith(".apk", true) -> out += Junk(f, size, Junk.Kind.Apk, "уже установлен или больше не нужен")
                size >= 100L * 1024 * 1024 -> out += Junk(f, size, Junk.Kind.Big, rel(f))
                f.absolutePath.startsWith(downloads) && now - f.lastModified() > 90L * 24 * 3600 * 1000 ->
                    out += Junk(f, size, Junk.Kind.OldDownloads, "скачан ${(now - f.lastModified()) / 86400000} дн. назад")
            }
        }
        empties.forEach { out += Junk(it, 0, Junk.Kind.Empty, rel(it)) }

        progress("Ищу копии…")
        // Same size → same head+tail hash → copies. The oldest one stays.
        all.filter { it.length() > 512 * 1024 }.groupBy { it.length() }.values.filter { it.size > 1 }.forEach { same ->
            same.groupBy { quickHash(it) }.values.filter { it.size > 1 }.forEach { copies ->
                val keep = copies.minBy { it.lastModified() }
                copies.filter { it != keep }.forEach { out += Junk(it, it.length(), Junk.Kind.Dupes, "копия «${keep.name}»", keep.absolutePath) }
            }
        }
        return out.sortedWith(compareBy<Junk> { it.kind.ordinal }.thenByDescending { it.size })
    }

    private fun dirJunk(d: File, kind: Junk.Kind, why: String) = Junk(d, d.walkTopDown().filter { it.isFile }.sumOf { it.length() }, kind, why)

    private fun rel(f: File) = f.absolutePath.removePrefix(root.absolutePath).trimStart('/')

    private fun quickHash(f: File): String = runCatching {
        val md = MessageDigest.getInstance("MD5")
        RandomAccessFile(f, "r").use { r ->
            val buf = ByteArray(64 * 1024)
            md.update(buf, 0, r.read(buf))
            r.seek((f.length() - buf.size).coerceAtLeast(0)); md.update(buf, 0, r.read(buf))
        }
        md.digest().joinToString("") { "%02x".format(it) }
    }.getOrDefault(f.absolutePath)

    /** Returns bytes freed. */
    fun delete(items: List<Junk>): Long = items.sumOf { j ->
        val ok = if (j.file.isDirectory) j.file.deleteRecursively() else j.file.delete()
        if (ok) j.size else 0L
    }

    data class Mem(val usedPct: Int, val freeGb: Double, val totalGb: Double, val storageFreeGb: Double, val storageTotalGb: Double)

    fun memory(c: Context): Mem {
        val mi = ActivityManager.MemoryInfo().also { c.getSystemService(ActivityManager::class.java).getMemoryInfo(it) }
        val st = android.os.StatFs(Environment.getDataDirectory().path)
        val gb = 1024.0 * 1024 * 1024
        return Mem(((mi.totalMem - mi.availMem) * 100 / mi.totalMem).toInt(), mi.availMem / gb, mi.totalMem / gb, st.availableBytes / gb, st.totalBytes / gb)
    }

    fun human(b: Long): String = when {
        b >= 1L shl 30 -> "%.1f ГБ".format(b / (1024.0 * 1024 * 1024))
        b >= 1L shl 20 -> "%.0f МБ".format(b / (1024.0 * 1024))
        b >= 1024 -> "%.0f КБ".format(b / 1024.0)
        else -> "$b Б"
    }
}
