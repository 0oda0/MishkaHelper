package com.mishka.helper

import android.Manifest
import android.annotation.SuppressLint
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.PowerManager
import android.provider.Settings
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.compose.setContent
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.core.app.NotificationManagerCompat
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONObject

private val Bg = Color(0xFF0B0B14)
private val Card = Color(0xFF151527)
private val Pink = Color(0xFFFF3DA8)
private val Cyan = Color(0xFF3DC8FF)
private val Dim = Color(0xFF9999AA)

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        LinkService.start(this)
        setContent {
            MaterialTheme(colorScheme = darkColorScheme(primary = Pink, secondary = Cyan, background = Bg, surface = Card)) {
                Surface(Modifier.fillMaxSize(), color = Bg) { App() }
            }
        }
    }
}

@Composable
private fun App() {
    var tab by rememberSaveable { mutableIntStateOf(0) }
    Scaffold(
        containerColor = Bg,
        bottomBar = {
            NavigationBar(containerColor = Color(0xFF10101E)) {
                listOf(Triple("ПК", Icons.Filled.Computer, 0), Triple("Пульт", Icons.Filled.TouchApp, 1), Triple("Чистка", Icons.Filled.CleaningServices, 2)).forEach { (label, icon, i) ->
                    NavigationBarItem(selected = tab == i, onClick = { tab = i }, icon = { Icon(icon, null) }, label = { Text(label) },
                        colors = NavigationBarItemDefaults.colors(selectedIconColor = Pink, selectedTextColor = Pink, indicatorColor = Color(0x33FF3DA8)))
                }
            }
        }
    ) { pad ->
        Box(Modifier.padding(pad)) {
            when (tab) { 0 -> PcTab(); 1 -> RemoteTab(); else -> CleanTab() }
        }
    }
}

@Composable
private fun Title(text: String) = Text(text, fontSize = 22.sp, fontWeight = FontWeight.Bold,
    style = LocalTextStyle.current.copy(brush = Brush.horizontalGradient(listOf(Pink, Cyan))), modifier = Modifier.padding(bottom = 12.dp))

@Composable
private fun NeonCard(content: @Composable ColumnScope.() -> Unit) = Column(
    Modifier.fillMaxWidth().padding(bottom = 12.dp).background(Card, RoundedCornerShape(18.dp))
        .border(1.dp, Color(0x55FF3DA8), RoundedCornerShape(18.dp)).padding(16.dp), content = content)

@Composable
private fun Hint(text: String) = Text(text, color = Dim, fontSize = 13.sp)

// ---------------- ПК: pairing, status, quick actions ----------------
@Composable
private fun PcTab() {
    val c = LocalContext.current
    val scope = rememberCoroutineScope()
    var paired by remember { mutableStateOf(Link.paired(c)) }
    val connected by LinkService.connected.collectAsState()
    val error by LinkService.lastError.collectAsState()
    var found by remember { mutableStateOf<List<Pc>>(emptyList()) }
    var searching by remember { mutableStateOf(false) }
    var chosen by remember { mutableStateOf<Pc?>(null) }
    var pin by remember { mutableStateOf("") }
    var manual by remember { mutableStateOf("") }
    var msg by remember { mutableStateOf("") }
    val notifPerm = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {}
    val pickFiles = rememberLauncherForActivityResult(ActivityResultContracts.GetMultipleContents()) { uris ->
        if (uris.isEmpty()) return@rememberLauncherForActivityResult
        msg = "Отправляю ${uris.size}…"
        scope.launch {
            val ok = withContext(Dispatchers.IO) { uris.count { runCatching { Link.upload(c, it) }.getOrDefault(false) } }
            msg = "На ПК отправлено: $ok из ${uris.size} (Загрузки\\С телефона)"
        }
    }

    fun search() {
        searching = true
        scope.launch { found = withContext(Dispatchers.IO) { runCatching { Link.discover() }.getOrDefault(emptyList()) }; searching = false }
    }
    LaunchedEffect(Unit) {
        if (!paired) search()
        if (Build.VERSION.SDK_INT >= 33) notifPerm.launch(Manifest.permission.POST_NOTIFICATIONS)
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(16.dp)) {
        Title("Mishka")
        if (!paired) {
            NeonCard {
                Text("Подключи компьютер", fontWeight = FontWeight.SemiBold, fontSize = 17.sp)
                Hint("Телефон и ПК в одной Wi-Fi сети. На ПК: Mishka Helper → Инструменты → «Пульт с телефона» → включить «по Wi-Fi».")
                Spacer(Modifier.height(10.dp))
                if (searching) LinearProgressIndicator(Modifier.fillMaxWidth(), color = Pink)
                found.forEach { pc ->
                    Row(Modifier.fillMaxWidth().clickable { chosen = pc }.padding(vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                        Icon(Icons.Filled.Computer, null, tint = if (chosen == pc) Pink else Cyan)
                        Spacer(Modifier.width(10.dp))
                        Column { Text(pc.name, fontWeight = FontWeight.SemiBold); Hint("${pc.host} · Mishka ${pc.version}") }
                    }
                }
                if (!searching && found.isEmpty()) Hint("Не нашёл ПК. Проверь, что пульт на ПК включён, или впиши адрес вручную (он показан там же).")
                Row { TextButton(onClick = { search() }) { Text("Искать снова") } }
                OutlinedTextField(manual, { manual = it }, label = { Text("Или адрес ПК, например 192.168.0.102") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                Spacer(Modifier.height(8.dp))
                OutlinedTextField(pin, { if (it.length <= 6) pin = it.filter(Char::isDigit) }, label = { Text("PIN с экрана ПК") }, singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.NumberPassword), modifier = Modifier.fillMaxWidth())
                Spacer(Modifier.height(10.dp))
                Button(onClick = {
                    val pc = chosen ?: manual.trim().takeIf { it.isNotEmpty() }?.let { Pc("ПК", it.substringBefore(":"), it.substringAfter(":", "8770").toIntOrNull() ?: 8770, "") }
                    if (pc == null || pin.length != 6) { msg = "Выбери ПК и введи 6 цифр PIN"; return@Button }
                    msg = "Проверяю…"
                    scope.launch {
                        val r = withContext(Dispatchers.IO) { runCatching { Link.call(c, "status", host = pc.host, port = pc.port, pin = pin) }.getOrNull() }
                        msg = when {
                            r == null -> "ПК не отвечает по ${pc.host}"
                            r.optString("error") == "pin" -> "Неверный PIN"
                            r.optString("error") == "locked" -> "Слишком много попыток — подожди минуту"
                            else -> { Link.save(c, pc.copy(name = r.optString("pc", pc.name)), pin); LinkService.start(c); paired = true; "Подключено!" }
                        }
                    }
                }, colors = ButtonDefaults.buttonColors(containerColor = Pink, contentColor = Color.White)) { Text("Подключить") }
                if (msg.isNotEmpty()) Hint(msg)
            }
            return@Column
        }

        NeonCard {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(10.dp).background(if (connected) Color(0xFF3DFF8A) else Color(0xFFFF5566), RoundedCornerShape(5.dp)))
                Spacer(Modifier.width(8.dp))
                Text(if (connected) "На связи: ${Link.pcName(c)}" else "Нет связи с ${Link.pcName(c)}", fontWeight = FontWeight.SemiBold, fontSize = 17.sp)
            }
            if (!connected && error.isNotEmpty()) Hint(error)
            Hint("${Link.host(c)}:${Link.port(c)}")
        }

        NeonCard {
            Text("Быстро", fontWeight = FontWeight.SemiBold)
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { pickFiles.launch("*/*") }, Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF22223A), contentColor = Color.White)) { Text("Файлы → ПК") }
                Button(onClick = {
                    val clip = c.getSystemService(ClipboardManager::class.java).primaryClip?.getItemAt(0)?.coerceToText(c)?.toString().orEmpty()
                    msg = if (clip.isEmpty()) "Буфер пуст" else if (LinkService.send(JSONObject().put("t", "clip").put("text", clip))) "Буфер на ПК" else "Нет связи"
                }, Modifier.weight(1f), colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF22223A), contentColor = Color.White)) { Text("Буфер → ПК") }
            }
            Hint("Ещё: «Поделиться → На ПК (Mishka)» в любом приложении. С ПК сюда: страница «Телефон» в Mishka Helper.")
            if (msg.isNotEmpty()) Text(msg, color = Cyan, fontSize = 13.sp)
        }

        NeonCard {
            Text("Уведомления на ПК", fontWeight = FontWeight.SemiBold)
            val on = NotificationManagerCompat.getEnabledListenerPackages(c).contains(c.packageName)
            Hint(if (on) "Включено: уведомления телефона появляются на компьютере." else "Выключено. Android спросит разрешение «Доступ к уведомлениям» — найди там Mishka.")
            if (!on) TextButton(onClick = { c.startActivity(Intent(Settings.ACTION_NOTIFICATION_LISTENER_SETTINGS)) }) { Text("Включить") }
        }

        NeonCard {
            Text("Чтобы связь не пропадала", fontWeight = FontWeight.SemiBold)
            val pm = c.getSystemService(PowerManager::class.java)
            val free = pm.isIgnoringBatteryOptimizations(c.packageName)
            Hint(if (free) "Экономия батареи не мешает связи." else "Android может усыплять связь с ПК. Разреши Mishka работать в фоне.")
            if (!free) TextButton(onClick = { c.startActivity(Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:${c.packageName}"))) }) { Text("Разрешить") }
        }

        TextButton(onClick = { LinkService.stop(c); Link.forget(c); paired = false; search() }) { Text("Отключить этот ПК", color = Dim) }
    }
}

// ---------------- Пульт: the PC's own remote page, logged in by PIN ----------------
@SuppressLint("SetJavaScriptEnabled")
@Composable
private fun RemoteTab() {
    val c = LocalContext.current
    if (!Link.paired(c)) { Box(Modifier.fillMaxSize().padding(24.dp)) { Hint("Сначала подключи ПК на вкладке «ПК».") }; return }
    AndroidView(factory = {
        WebView(it).apply {
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            setBackgroundColor(0xFF0B0B14.toInt())
            webViewClient = WebViewClient()
            loadUrl("${Link.base(c)}/#pin=${Link.pin(c)}")
        }
    }, modifier = Modifier.fillMaxSize())
}

// ---------------- Чистка ----------------
@Composable
private fun CleanTab() {
    val c = LocalContext.current
    val scope = rememberCoroutineScope()
    var access by remember { mutableStateOf(Cleaner.hasAccess()) }
    var items by remember { mutableStateOf<List<Junk>>(emptyList()) }
    val selected = remember { mutableStateMapOf<String, Boolean>() }
    var status by remember { mutableStateOf("") }
    var busy by remember { mutableStateOf(false) }
    var confirm by remember { mutableStateOf(false) }
    val mem = remember(busy) { Cleaner.memory(c) }

    LifecycleResume { access = Cleaner.hasAccess() }

    fun scan() {
        busy = true; items = emptyList()
        scope.launch {
            val found = withContext(Dispatchers.IO) { Cleaner.scan { p -> scope.launch { status = p } } }
            items = found
            selected.clear(); found.forEach { selected[it.file.absolutePath] = it.kind.safe }
            status = "Найдено: ${Cleaner.human(found.sumOf { it.size })}. Отмечено безопасное — проверь и удаляй."
            busy = false
        }
    }

    Column(Modifier.fillMaxSize().padding(16.dp)) {
        Title("Чистка")
        NeonCard {
            Text("Память ${mem.usedPct}%% · свободно %.1f из %.0f ГБ".format(mem.freeGb, mem.totalGb), fontWeight = FontWeight.SemiBold)
            Text("Хранилище: свободно %.1f из %.0f ГБ".format(mem.storageFreeGb, mem.storageTotalGb))
            Hint("Оперативку Android освобождает сам — «ускорители памяти» на нём ничего не дают. Кэш чужих приложений чистится только в настройках:")
            TextButton(onClick = { c.startActivity(Intent(Settings.ACTION_INTERNAL_STORAGE_SETTINGS)) }) { Text("Открыть «Хранилище»") }
        }
        if (!access) {
            NeonCard {
                Text("Нужен доступ к файлам", fontWeight = FontWeight.SemiBold)
                Hint("Чтобы найти большие файлы, старые APK и копии во всей памяти телефона. Без этого чистка не видит файлов.")
                Button(onClick = { c.startActivity(Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION, Uri.parse("package:${c.packageName}"))) },
                    colors = ButtonDefaults.buttonColors(containerColor = Pink, contentColor = Color.White)) { Text("Разрешить") }
            }
            return@Column
        }
        Row(verticalAlignment = Alignment.CenterVertically) {
            Button(onClick = { scan() }, enabled = !busy, colors = ButtonDefaults.buttonColors(containerColor = Pink, contentColor = Color.White)) { Text(if (items.isEmpty()) "Анализ" else "Заново") }
            Spacer(Modifier.width(8.dp))
            val sum = items.filter { selected[it.file.absolutePath] == true }.sumOf { it.size }
            if (items.isNotEmpty()) Button(onClick = { confirm = true }, enabled = !busy && sum >= 0 && selected.values.any { it },
                colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF4A1530), contentColor = Color.White)) { Text("Удалить ${Cleaner.human(sum)}") }
        }
        if (busy) LinearProgressIndicator(Modifier.fillMaxWidth().padding(top = 8.dp), color = Pink)
        if (status.isNotEmpty()) Hint(status)
        LazyColumn(Modifier.fillMaxSize().padding(top = 8.dp)) {
            items.groupBy { it.kind }.forEach { (kind, list) ->
                item {
                    Text("${kind.title} · ${Cleaner.human(list.sumOf { it.size })}", color = Cyan, fontWeight = FontWeight.SemiBold, modifier = Modifier.padding(top = 10.dp, bottom = 4.dp))
                }
                items(list, key = { it.file.absolutePath }) { j ->
                    Row(Modifier.fillMaxWidth().clickable { selected[j.file.absolutePath] = selected[j.file.absolutePath] != true }.padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(selected[j.file.absolutePath] == true, { selected[j.file.absolutePath] = it }, colors = CheckboxDefaults.colors(checkedColor = Pink))
                        Column(Modifier.weight(1f)) { Text(j.file.name, maxLines = 1, fontSize = 14.sp); Hint(j.why) }
                        Text(Cleaner.human(j.size), color = Dim, fontSize = 13.sp)
                    }
                }
            }
        }
    }

    if (confirm) {
        val chosen = items.filter { selected[it.file.absolutePath] == true }
        AlertDialog(onDismissRequest = { confirm = false },
            title = { Text("Удалить ${chosen.size} шт. (${Cleaner.human(chosen.sumOf { it.size })})?") },
            text = { Text("Файлы удаляются насовсем, без корзины.") },
            confirmButton = {
                TextButton(onClick = {
                    confirm = false; busy = true
                    scope.launch {
                        val freed = withContext(Dispatchers.IO) { Cleaner.delete(chosen) }
                        items = items - chosen.toSet()
                        status = "Освобождено ${Cleaner.human(freed)}"
                        busy = false
                    }
                }) { Text("Удалить", color = Pink) }
            },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text("Отмена") } })
    }
}

/** Re-check permissions when the user comes back from a settings screen. */
@Composable
private fun LifecycleResume(onResume: () -> Unit) {
    @Suppress("DEPRECATION") val owner = androidx.compose.ui.platform.LocalLifecycleOwner.current
    DisposableEffect(owner) {
        val obs = androidx.lifecycle.LifecycleEventObserver { _, e -> if (e == androidx.lifecycle.Lifecycle.Event.ON_RESUME) onResume() }
        owner.lifecycle.addObserver(obs)
        onDispose { owner.lifecycle.removeObserver(obs) }
    }
}
