package com.mishka.helper

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.ExperimentalFoundationApi
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import kotlinx.coroutines.flow.MutableStateFlow
import org.json.JSONArray
import org.json.JSONObject

data class DeckKey(val slot: Int, val label: String, val emoji: String, val color: String, val kind: String, val value: String, val live: String,
                   val style: String = "", val w: Int = 1, val h: Int = 1) {
    fun json(): JSONObject = JSONObject().put("slot", slot).put("label", label).put("emoji", emoji).put("color", color).put("kind", kind).put("value", value).put("live", live)
        .put("style", style).put("w", w).put("h", h)
}
data class DeckPage(val id: String, val name: String, val processes: List<String>, val keys: List<DeckKey>) {
    fun json(): JSONObject = JSONObject().put("id", id).put("name", name).put("processes", JSONArray(processes)).put("keys", JSONArray(keys.map { it.json() }))
}
data class DeckData(val cols: Int, val rows: Int, val autoPage: Boolean, val pages: List<DeckPage>) {
    fun json(): JSONObject = JSONObject().put("cols", cols).put("rows", rows).put("autoPage", autoPage).put("pages", JSONArray(pages.map { it.json() }))
}

/**
 * The stream deck: the PC sends the pages (and what its editor can offer), live numbers every second and the page for the program in front;
 * the phone sends presses and edits. All over LinkService's WebSocket.
 */
object Deck {
    val data = MutableStateFlow<DeckData?>(null)
    val live = MutableStateFlow(JSONObject())
    val autoPage = MutableStateFlow("")             // the page the PC asks for (program in front changed)
    val actions = MutableStateFlow<List<Pair<String, String>>>(emptyList())   // action key → name
    val lives = MutableStateFlow<List<Pair<String, String>>>(emptyList())
    val styles = MutableStateFlow<List<Pair<String, String>>>(emptyList())
    /** The last minute of every live number (one a second), for graphs. */
    val history = MutableStateFlow<Map<String, List<Float>>>(emptyMap())
    val icons = MutableStateFlow<Map<String, Bitmap>>(emptyMap())

    /** From LinkService (main thread). */
    fun handle(m: JSONObject) {
        when (m.optString("t")) {
            "deck" -> {
                data.value = parse(m.getJSONObject("deck"))
                actions.value = m.optJSONArray("actions")?.let { a -> (0 until a.length()).map { a.getJSONObject(it).let { o -> o.optString("a") to o.optString("n") } } }.orEmpty()
                styles.value = m.optJSONArray("styles")?.let { a -> (0 until a.length()).map { a.getJSONObject(it).let { o -> o.optString("k") to o.optString("n") } } }.orEmpty()
                lives.value = m.optJSONArray("lives")?.let { a -> (0 until a.length()).map { a.getJSONObject(it).let { o -> o.optString("k") to o.optString("n") } } }.orEmpty()
                icons.value = m.optJSONObject("icons")?.let { o ->
                    o.keys().asSequence().mapNotNull { k -> runCatching { Base64.decode(o.getString(k), Base64.DEFAULT).let { b -> k to BitmapFactory.decodeByteArray(b, 0, b.size) } }.getOrNull() }.toMap()
                }.orEmpty()
            }
            "deckLive" -> {
                val v = m.optJSONObject("v") ?: JSONObject()
                live.value = v
                val h = history.value.toMutableMap()
                v.keys().asSequence().filter { it.startsWith("n:") }.forEach { k -> h[k] = ((h[k] ?: emptyList()) + v.optDouble(k).toFloat()).takeLast(60) }
                history.value = h
            }
            "deckPage" -> autoPage.value = m.optString("id")
        }
    }

    fun parse(o: JSONObject): DeckData {
        val pages = o.optJSONArray("pages") ?: JSONArray()
        return DeckData(o.optInt("cols", 4), o.optInt("rows", 3), o.optBoolean("autoPage", true), (0 until pages.length()).map { i ->
            val p = pages.getJSONObject(i)
            val keys = p.optJSONArray("keys") ?: JSONArray()
            val procs = p.optJSONArray("processes") ?: JSONArray()
            DeckPage(p.optString("id"), p.optString("name"), (0 until procs.length()).map { procs.getString(it) }, (0 until keys.length()).map { j ->
                val k = keys.getJSONObject(j)
                DeckKey(k.optInt("slot"), k.optString("label"), k.optString("emoji"), k.optString("color"), k.optString("kind"), k.optString("value"), k.optString("live"),
                    k.optString("style"), k.optInt("w", 1).coerceAtLeast(1), k.optInt("h", 1).coerceAtLeast(1))
            })
        })
    }

    fun press(page: String, slot: Int) = LinkService.send(JSONObject().put("t", "deckPress").put("page", page).put("slot", slot))
    fun save(d: DeckData) { data.value = d; LinkService.send(JSONObject().put("t", "deckSave").put("deck", d.json())) }
    fun on() = LinkService.send(JSONObject().put("t", "deckOn"))
    fun off() = LinkService.send(JSONObject().put("t", "deckOff"))
}

private val Bg = Color(0xFF0B0B14)
private val KeyBg = Color(0xFF181830)
private val Pink = Color(0xFFFF3DA8)
private val Cyan = Color(0xFF3DC8FF)
private val Red = Color(0xFFFF5566)
private val Dim = Color(0xFF9999AA)

private fun color(hex: String, fallback: Color) = runCatching { Color(android.graphics.Color.parseColor(hex)) }.getOrDefault(fallback)

/** «Деск» tab: the grid fills the screen (phone or tablet, either orientation); the screen stays on while it's open. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
fun DeckTab() {
    val view = LocalView.current
    val haptic = LocalHapticFeedback.current
    val connected by LinkService.connected.collectAsState()
    val deck by Deck.data.collectAsState()
    val live by Deck.live.collectAsState()
    val auto by Deck.autoPage.collectAsState()
    val icons by Deck.icons.collectAsState()
    val history by Deck.history.collectAsState()
    var pageId by rememberSaveable { mutableStateOf("") }
    var editing by remember { mutableStateOf<Int?>(null) }
    var pageMenu by remember { mutableStateOf(false) }
    val ctx = androidx.compose.ui.platform.LocalContext.current
    val prefs = remember { ctx.getSharedPreferences("deck", android.content.Context.MODE_PRIVATE) }
    // This device's own grid (a tablet fits more than a phone); 0 = the size set on the PC.
    var myCols by remember { mutableIntStateOf(prefs.getInt("cols", 0)) }
    var myRows by remember { mutableIntStateOf(prefs.getInt("rows", 0)) }
    var gridMenu by remember { mutableStateOf(false) }

    DisposableEffect(Unit) { view.keepScreenOn = true; onDispose { view.keepScreenOn = false; Deck.off() } }
    LaunchedEffect(connected) { if (connected) Deck.on() }   // also after a reconnect
    LaunchedEffect(auto) { if (auto.isNotEmpty()) pageId = auto }

    // A PC Mishka older than the deck never answers deckOn.
    var slow by remember { mutableStateOf(false) }
    LaunchedEffect(connected, deck) { slow = false; if (connected && deck == null) { kotlinx.coroutines.delay(5000); slow = true } }

    val d = deck
    if (!connected || d == null) {
        Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
            Text(when {
                !connected -> "Нет связи с ПК. Подключи его на вкладке «ПК»."
                slow -> "ПК не отвечает на стримдек. Обнови Mishka Helper на ПК до версии 2.1 или новее."
                else -> "Загружаю кнопки…"
            }, color = Dim, textAlign = TextAlign.Center)
        }
        return
    }
    val page = d.pages.firstOrNull { it.id == pageId } ?: d.pages.first()

    Column(Modifier.fillMaxSize().background(Bg).padding(8.dp)) {
        Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
          Row(Modifier.weight(1f).horizontalScroll(rememberScrollState()), verticalAlignment = Alignment.CenterVertically) {
            d.pages.forEach { p ->
                val sel = p.id == page.id
                Text(p.name, color = if (sel) Color.White else Dim, fontWeight = if (sel) FontWeight.SemiBold else FontWeight.Normal,
                    modifier = Modifier.padding(end = 6.dp).background(if (sel) Color(0x55FF3DA8) else Color.Transparent, RoundedCornerShape(14.dp))
                        .combinedClickable(onClick = { pageId = p.id }, onLongClick = { pageId = p.id; pageMenu = true }).padding(horizontal = 12.dp, vertical = 6.dp))
            }
            Text("＋", color = Cyan, fontSize = 18.sp, modifier = Modifier.clickable {
                val id = java.util.UUID.randomUUID().toString().take(8)
                Deck.save(d.copy(pages = d.pages + DeckPage(id, "Страница ${d.pages.size + 1}", emptyList(), emptyList()))); pageId = id
            }.padding(horizontal = 10.dp, vertical = 4.dp))
          }
          Text("⚙", color = Dim, fontSize = 20.sp, modifier = Modifier.clickable { gridMenu = true }.padding(horizontal = 10.dp, vertical = 4.dp))
        }
        Spacer(Modifier.height(6.dp))
        BoxWithConstraints(Modifier.fillMaxSize()) {
            val gap = 8.dp
            // Held upright, a 4×3 deck becomes 3×4: same keys in the same reading order, just wrapped the other way.
            val gc = if (myCols > 0) myCols else d.cols
            val gr = if (myRows > 0) myRows else d.rows
            val turn = maxHeight > maxWidth && gc > gr
            val cols = if (turn) gr else gc
            val rows = if (turn) gc else gr
            val w = (maxWidth - gap * (cols - 1)) / cols
            val h = (maxHeight - gap * (rows - 1)) / rows
            // Keys may take several cells (a graph on 2 × 1): each is placed by its slot, the cells it covers are skipped.
            val covered = HashSet<Int>()
            Box(Modifier.fillMaxSize()) {
                for (slot in 0 until cols * rows) {
                    if (slot in covered) continue
                    val r = slot / cols; val c = slot % cols
                    val k = page.keys.firstOrNull { it.slot == slot }
                    val kw = (k?.w ?: 1).coerceIn(1, cols - c)
                    val kh = (k?.h ?: 1).coerceIn(1, rows - r)
                    for (dy in 0 until kh) for (dx in 0 until kw) covered += (r + dy) * cols + c + dx
                    val bw = w * kw + gap * (kw - 1); val bh = h * kh + gap * (kh - 1)
                    Box(Modifier.offset(x = (w + gap) * c, y = (h + gap) * r).size(bw, bh)) {
                        DeckKeyView(k, bw, bh, live, history, icons,
                            onClick = {
                                if (k == null) { editing = slot; return@DeckKeyView }
                                haptic.performHapticFeedback(HapticFeedbackType.LongPress)
                                if (k.kind == "page") pageId = k.value else Deck.press(page.id, slot)
                            },
                            onLongClick = { editing = slot })
                    }
                }
            }
        }
    }

    editing?.let { slot -> KeyEditor(d, page, slot) { editing = null } }
    if (gridMenu) GridEditor(if (myCols > 0) myCols else d.cols, if (myRows > 0) myRows else d.rows, onDone = { c, r ->
        myCols = c; myRows = r; prefs.edit().putInt("cols", c).putInt("rows", r).apply(); gridMenu = false
    }, onCancel = { gridMenu = false })
    if (pageMenu) PageEditor(d, page, onDone = { pageMenu = false }, onDeleted = { pageId = d.pages.first().id })
}

private val Kinds = listOf("act" to "Действие Mishka", "keys" to "Сочетание клавиш", "text" to "Вставить текст", "open" to "Открыть программу или сайт", "page" to "Перейти на страницу", "" to "Пусто")
private val Swatches = listOf("", "#FF3DA8", "#3DC8FF", "#3DFF8A", "#FFB23D", "#B03DFF", "#FF5566", "#22223A")

/** Long press on a key: what it does and how it looks. Saved to the PC, which checks it and sends it back. */
@OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
private fun KeyEditor(d: DeckData, page: DeckPage, slot: Int, close: () -> Unit) {
    val k = page.keys.firstOrNull { it.slot == slot }
    var kind by remember { mutableStateOf(k?.kind ?: "act") }
    var value by remember { mutableStateOf(k?.value ?: "") }
    var label by remember { mutableStateOf(k?.label ?: "") }
    var emoji by remember { mutableStateOf(k?.emoji ?: "") }
    var color by remember { mutableStateOf(k?.color ?: "") }
    var liveKey by remember { mutableStateOf(k?.live ?: "") }
    var style by remember { mutableStateOf(k?.style ?: "") }
    var kw by remember { mutableIntStateOf(k?.w ?: 1) }
    var kh by remember { mutableIntStateOf(k?.h ?: 1) }
    val styles by Deck.styles.collectAsState()
    var query by remember { mutableStateOf("") }
    var picking by remember { mutableStateOf(k?.kind != "act") }
    val actions by Deck.actions.collectAsState()
    val lives by Deck.lives.collectAsState()

    AlertDialog(onDismissRequest = close, containerColor = Color(0xFF151527),
        title = { Text(if (k == null) "Новая кнопка" else "Кнопка «${k.label}»") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                // Chips in one scrolling line: a phone held sideways has little height for a list.
                FlowRow {
                    Kinds.forEach { (kk, n) ->
                        FilterChip(kind == kk, { kind = kk; if (kk != k?.kind) value = "" }, { Text(n, fontSize = 13.sp) }, Modifier.padding(end = 6.dp),
                            colors = FilterChipDefaults.filterChipColors(selectedContainerColor = Color(0x55FF3DA8)))
                    }
                }
                Spacer(Modifier.height(8.dp))
                when (kind) {
                    "act" -> if (!picking && value.isNotEmpty()) {
                        // Picked: one line, so the settings below stay in reach (a list here would take every swipe).
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text("Выбрано: " + (actions.firstOrNull { it.first == value }?.second ?: value), color = Pink, fontSize = 14.sp, modifier = Modifier.weight(1f))
                            TextButton(onClick = { picking = true }) { Text("Другое") }
                        }
                    } else {
                        OutlinedTextField(query, { query = it }, label = { Text("Найти действие") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                        val list = actions.filter { query.isBlank() || it.second.contains(query, true) }.take(40)
                        Column(Modifier.heightIn(max = 160.dp).verticalScroll(rememberScrollState())) {
                            list.forEach { (a, n) ->
                                Text(n, color = if (a == value) Pink else Color.White, fontSize = 14.sp, modifier = Modifier.fillMaxWidth()
                                    .clickable { value = a; picking = false; if (label.isEmpty() || actions.any { it.second == label }) label = n.substringAfter(": ") }.padding(vertical = 6.dp))
                            }
                        }
                    }
                    "keys" -> OutlinedTextField(value, { value = it }, label = { Text("ctrl+shift+m · f13 · alt w f f") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    "text" -> OutlinedTextField(value, { value = it }, label = { Text("Текст") }, modifier = Modifier.fillMaxWidth())
                    "open" -> OutlinedTextField(value, { value = it }, label = { Text("Путь на ПК или адрес сайта") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    "page" -> d.pages.forEach { p ->
                        Text(p.name, color = if (p.id == value) Pink else Color.White, modifier = Modifier.fillMaxWidth().clickable { value = p.id }.padding(vertical = 6.dp))
                    }
                }
                if (kind.isNotEmpty()) {
                    OutlinedTextField(label, { label = it }, label = { Text("Подпись") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    OutlinedTextField(emoji, { emoji = it.take(16) }, label = { Text("Значок-эмодзи") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                    Spacer(Modifier.height(8.dp))
                    Row { Swatches.forEach { s -> Box(Modifier.padding(end = 6.dp).size(28.dp).background(color(s, KeyBg), RoundedCornerShape(8.dp))
                        .border(if (s == color) 2.dp else 1.dp, if (s == color) Color.White else Color(0x33FFFFFF), RoundedCornerShape(8.dp)).clickable { color = s }) } }
                    Spacer(Modifier.height(8.dp))
                    Text("Показывать на кнопке", color = Dim, fontSize = 12.sp)
                    FlowRow {
                        lives.forEach { (lk, ln) -> FilterChip(liveKey == lk, { liveKey = lk }, { Text(ln, fontSize = 12.sp) }, Modifier.padding(end = 4.dp)) }
                    }
                    if (liveKey.isNotEmpty()) {
                        Text("Как показывать", color = Dim, fontSize = 12.sp)
                        FlowRow {
                            styles.forEach { (sk, sn) -> FilterChip(style == sk, { style = sk; if (sk == "graph" && kw == 1 && kh == 1) kw = 2 }, { Text(sn, fontSize = 12.sp) }, Modifier.padding(end = 4.dp)) }
                        }
                    }
                    Text("Размер в клетках", color = Dim, fontSize = 12.sp)
                    FlowRow {
                        listOf(1 to 1, 2 to 1, 1 to 2, 2 to 2, 3 to 1, 4 to 2).forEach { (a, b) ->
                            FilterChip(kw == a && kh == b, { kw = a; kh = b }, { Text("$a×$b", fontSize = 12.sp) }, Modifier.padding(end = 4.dp))
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val keys = page.keys.filter { it.slot != slot } + (if (kind.isEmpty() || (kind != "text" && value.isBlank())) emptyList() else listOf(DeckKey(slot, label.trim(), emoji.trim(), color, kind, value.trim(), liveKey, if (liveKey.isEmpty()) "" else style, kw, kh)))
                Deck.save(d.copy(pages = d.pages.map { if (it.id == page.id) it.copy(keys = keys) else it }))
                close()
            }) { Text("Сохранить", color = Pink) }
        },
        dismissButton = { TextButton(onClick = close) { Text("Отмена") } })
}

/** Long press on a page's name: rename, the programs that bring it up, delete. */
@Composable
private fun PageEditor(d: DeckData, page: DeckPage, onDone: () -> Unit, onDeleted: () -> Unit) {
    var name by remember { mutableStateOf(page.name) }
    var procs by remember { mutableStateOf(page.processes.joinToString(", ")) }
    AlertDialog(onDismissRequest = onDone, containerColor = Color(0xFF151527), title = { Text("Страница") },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                OutlinedTextField(name, { name = it }, label = { Text("Название") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(procs, { procs = it }, label = { Text("Открывать при программах на ПК") }, modifier = Modifier.fillMaxWidth())
                Text("Имена процессов через запятую: photoshop, winword. game — любая игра в игровом режиме.", color = Dim, fontSize = 12.sp)
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val p = page.copy(name = name.trim().ifEmpty { page.name }, processes = procs.split(",").map { it.trim().removeSuffix(".exe") }.filter { it.isNotEmpty() })
                Deck.save(d.copy(pages = d.pages.map { if (it.id == page.id) p else it })); onDone()
            }) { Text("Сохранить", color = Pink) }
        },
        dismissButton = {
            Row {
                if (d.pages.size > 1) TextButton(onClick = { Deck.save(d.copy(pages = d.pages.filter { it.id != page.id })); onDeleted(); onDone() }) { Text("Удалить", color = Red) }
                TextButton(onClick = onDone) { Text("Отмена") }
            }
        })
}

/** ⚙: how many keys this device shows. Keys go in reading order, so a phone shows the first 12 and a tablet the first 24 of the same page. */
@Composable
private fun GridEditor(cols0: Int, rows0: Int, onDone: (Int, Int) -> Unit, onCancel: () -> Unit) {
    var cols by remember { mutableIntStateOf(cols0) }
    var rows by remember { mutableIntStateOf(rows0) }
    @Composable fun Stepper(label: String, v: Int, set: (Int) -> Unit) = Row(verticalAlignment = Alignment.CenterVertically) {
        Text(label, modifier = Modifier.width(110.dp))
        TextButton(onClick = { set((v - 1).coerceAtLeast(2)) }) { Text("−", fontSize = 22.sp, color = Pink) }
        Text("$v", fontSize = 20.sp, fontWeight = FontWeight.Bold, modifier = Modifier.width(36.dp), textAlign = TextAlign.Center)
        TextButton(onClick = { set((v + 1).coerceAtMost(8)) }) { Text("+", fontSize = 22.sp, color = Pink) }
    }
    AlertDialog(onDismissRequest = onCancel, containerColor = Color(0xFF151527), title = { Text("Сетка на этом устройстве") },
        text = {
            Column {
                Stepper("Столбцов", cols) { cols = it }
                Stepper("Рядов", rows) { rows = it }
                Text("Кнопок на экране: ${cols * rows}. Кнопки общие для всех устройств и идут по порядку: на телефоне видны первые, на планшете больше. " +
                    "Вертикально сетка сама поворачивается.", color = Dim, fontSize = 12.sp)
            }
        },
        confirmButton = { TextButton(onClick = { onDone(cols, rows) }) { Text("Готово", color = Pink) } },
        dismissButton = { TextButton(onClick = { onDone(0, 0) }) { Text("Как на ПК") } })
}

/** One key: its face (emoji or the program's icon, label) and, for a live number, the number itself, a column, a ring or the last minute's graph. */
@OptIn(ExperimentalFoundationApi::class)
@Composable
private fun DeckKeyView(k: DeckKey?, w: androidx.compose.ui.unit.Dp, h: androidx.compose.ui.unit.Dp, live: JSONObject, history: Map<String, List<Float>>,
                        icons: Map<String, Bitmap>, onClick: () -> Unit, onLongClick: () -> Unit) {
    val on = k != null && k.kind == "act" && live.optBoolean("s:" + k.value, false)
    // Off-switches (mic, sound) light up red when they are "on" = muted.
    val glow = if (!on) null else if (k!!.value in setOf("micMute", "mute")) Red else Pink
    val shape = RoundedCornerShape(16.dp)
    Box(Modifier.fillMaxSize().clip(shape).background(color(k?.color ?: "", KeyBg))
        .border(if (glow != null) 2.dp else 1.dp, glow ?: Color(0x33FF3DA8), shape)
        .combinedClickable(onClick = onClick, onLongClick = onLongClick), contentAlignment = Alignment.Center) {
        if (k == null) { Text("+", color = Color(0x33FFFFFF), fontSize = 22.sp); return@Box }
        val hist = history["n:" + k.live].orEmpty()
        val value = live.optDouble("n:" + k.live, Double.NaN).toFloat()
        // Full scale: sent for percents and degrees; FPS and network scale to what the last minute reached.
        val max = live.optDouble("m:" + k.live, 0.0).toFloat().let { if (it > 0f) it else (hist.maxOrNull() ?: 1f).coerceAtLeast(1f) * 1.15f }
        val frac = if (value.isNaN()) 0f else (value / max).coerceIn(0f, 1f)
        val level = if (frac > 0.85f) Red else if (frac > 0.6f) Color(0xFFFFB23D) else Cyan
        when (k.style) {
            "bar" -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.BottomCenter) {   // a column filling from the bottom
                Box(Modifier.fillMaxWidth().fillMaxHeight(frac).background(level.copy(alpha = 0.28f)))
            }
            "graph" -> androidx.compose.foundation.Canvas(Modifier.fillMaxSize().padding(top = h * 0.35f)) {
                if (hist.size < 2) return@Canvas
                val step = size.width / 59f
                val x0 = size.width - step * (hist.size - 1)
                val path = androidx.compose.ui.graphics.Path()
                hist.forEachIndexed { i, v -> val x = x0 + step * i; val y = size.height * (1f - (v / max).coerceIn(0f, 1f)); if (i == 0) path.moveTo(x, y) else path.lineTo(x, y) }
                val fill = androidx.compose.ui.graphics.Path().apply { addPath(path); lineTo(size.width, size.height); lineTo(x0, size.height); close() }
                drawPath(fill, level.copy(alpha = 0.18f))
                drawPath(path, level, style = androidx.compose.ui.graphics.drawscope.Stroke(width = 2.5.dp.toPx()))
            }
        }
        val low = h < 96.dp
        val icon = if (k.emoji.isEmpty() && k.kind == "open") icons[k.value] else null
        val graph = k.style == "graph" && k.live.isNotEmpty()
        Column(horizontalAlignment = Alignment.CenterHorizontally, modifier = Modifier.padding(4.dp).let { if (graph) it.align(Alignment.TopCenter) else it }) {
            @Composable fun Face() {
                if (icon != null) Image(icon.asImageBitmap(), null, Modifier.size(minOf(w, h) * (if (low || graph) 0.24f else 0.38f)))
                else if (k.emoji.isNotEmpty()) Text(k.emoji, fontSize = (minOf(w.value, h.value) * (if (low || graph) 0.2f else 0.3f)).coerceIn(14f, 54f).sp)
            }
            @Composable fun Live() {
                // The ring holds the number inside it, so it never runs over the label.
                if (k.live.isNotEmpty() && k.style == "ring") Box(Modifier.size(minOf(w, h) * (if (low) 0.55f else 0.5f)), contentAlignment = Alignment.Center) {
                    androidx.compose.foundation.Canvas(Modifier.fillMaxSize()) {
                        val st = androidx.compose.ui.graphics.drawscope.Stroke(width = 4.dp.toPx(), cap = androidx.compose.ui.graphics.StrokeCap.Round)
                        drawArc(Color(0x22FFFFFF), 135f, 270f, false, style = st)
                        drawArc(level, 135f, 270f * frac, false, style = st)
                    }
                    Text(live.optString("l:" + k.live, "…"), color = Color.White, fontWeight = FontWeight.Bold, fontSize = 13.sp)
                }
                else if (k.live.isNotEmpty()) Text(live.optString("l:" + k.live, "…"), color = if (k.style.isEmpty()) Cyan else Color.White, fontWeight = FontWeight.Bold,
                    fontSize = (if (k.w > 1 || k.h > 1) 20 else 16).sp, modifier = Modifier.padding(horizontal = 4.dp))
            }
            if (low || graph) Row(verticalAlignment = Alignment.CenterVertically) { Face(); Live(); if (graph) Text(" " + k.label, color = Color.White, fontSize = 12.sp, maxLines = 1) }
            else { Face(); Live() }
            if (!graph) Text(k.label, color = Color.White, fontSize = 12.sp, maxLines = if (low) 1 else 2, textAlign = TextAlign.Center, overflow = TextOverflow.Ellipsis)
        }
    }
}
