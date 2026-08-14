// uplr_converter.cpp — 旧版 .uplr（纯文本）→ 新版 .uplr（ZIP 容器）转换器
//
// 零第三方依赖，C++17，仅 Windows。
//
// 两种运行模式：
//   1) TUI 模式（无参数启动）：鼠标可点击的交互菜单，选择输入/输出、执行转换；
//   2) 命令行模式：uplr_converter.exe <input.uplr> <output.uplr>
//
// ZIP 使用 STORE（不压缩）写入，UTF-8 文件名（bit 11 置位）。

#include <windows.h>
#include <wchar.h>

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

namespace fs = std::filesystem;

// ===================== CRC32（zlib 兼容查表） =====================

static uint32_t s_crc_table[256];
static bool s_crc_ready = false;

static void crc_init() {
    for (uint32_t i = 0; i < 256; i++) {
        uint32_t c = i;
        for (int k = 0; k < 8; k++) {
            c = (c & 1) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
        }
        s_crc_table[i] = c;
    }
    s_crc_ready = true;
}

static uint32_t crc32_bytes(const uint8_t* data, size_t len) {
    if (!s_crc_ready) crc_init();
    uint32_t c = 0xFFFFFFFFu;
    for (size_t i = 0; i < len; i++) {
        c = s_crc_table[(c ^ data[i]) & 0xFF] ^ (c >> 8);
    }
    return c ^ 0xFFFFFFFFu;
}

// ===================== ZIP 写入（STORE） =====================

struct ZipEntry {
    std::string name;
    std::vector<uint8_t> data;
};

static void wr_u16(std::vector<uint8_t>& v, size_t off, uint16_t val) {
    v[off] = val & 0xFF;
    v[off + 1] = (val >> 8) & 0xFF;
}

static void wr_u32(std::vector<uint8_t>& v, size_t off, uint32_t val) {
    v[off] = val & 0xFF;
    v[off + 1] = (val >> 8) & 0xFF;
    v[off + 2] = (val >> 16) & 0xFF;
    v[off + 3] = (val >> 24) & 0xFF;
}

static bool write_zip(const fs::path& out_path, const std::vector<ZipEntry>& entries) {
    std::vector<uint8_t> body;   // 本地文件头 + 数据
    std::vector<uint8_t> central;
    uint32_t offset = 0;

    for (const auto& e : entries) {
        const uint32_t crc = crc32_bytes(e.data.data(), e.data.size());
        const uint32_t size = static_cast<uint32_t>(e.data.size());

        // ---- 本地文件头 ----
        const size_t base = body.size();
        body.resize(base + 30);
        body[base + 0] = 'P'; body[base + 1] = 'K';
        body[base + 2] = 0x03; body[base + 3] = 0x04;
        wr_u16(body, base + 4, 20);        // 版本
        wr_u16(body, base + 6, 0x0800);    // UTF-8 文件名
        wr_u16(body, base + 8, 0);         // 方法 STORE
        wr_u16(body, base + 10, 0);        // 时间
        wr_u16(body, base + 12, 0);        // 日期
        wr_u32(body, base + 14, crc);
        wr_u32(body, base + 18, size);     // 压缩大小
        wr_u32(body, base + 22, size);     // 原始大小
        wr_u16(body, base + 26, static_cast<uint16_t>(e.name.size()));
        wr_u16(body, base + 28, 0);        // 扩展区
        body.insert(body.end(), e.name.begin(), e.name.end());
        body.insert(body.end(), e.data.begin(), e.data.end());

        // ---- 中央目录条目 ----
        const size_t cb = central.size();
        central.resize(cb + 46);
        central[cb + 0] = 'P'; central[cb + 1] = 'K';
        central[cb + 2] = 0x01; central[cb + 3] = 0x02;
        wr_u16(central, cb + 4, 20);       // 制作者版本
        wr_u16(central, cb + 6, 20);       // 需要版本
        wr_u16(central, cb + 8, 0x0800);   // UTF-8
        wr_u16(central, cb + 10, 0);       // 方法
        wr_u16(central, cb + 12, 0);       // 时间
        wr_u16(central, cb + 14, 0);       // 日期
        wr_u32(central, cb + 16, crc);
        wr_u32(central, cb + 20, size);
        wr_u32(central, cb + 24, size);
        wr_u16(central, cb + 28, static_cast<uint16_t>(e.name.size()));
        wr_u16(central, cb + 30, 0);       // 扩展区
        wr_u16(central, cb + 32, 0);       // 注释
        wr_u16(central, cb + 34, 0);       // 磁盘号
        wr_u16(central, cb + 36, 0);       // 内部属性
        wr_u32(central, cb + 38, 0);       // 外部属性
        wr_u32(central, cb + 42, offset);  // 本地头偏移
        central.insert(central.end(), e.name.begin(), e.name.end());

        offset += 30 + static_cast<uint32_t>(e.name.size()) + size;
    }

    // ---- 中央目录结束记录 ----
    const uint32_t central_size = static_cast<uint32_t>(central.size());
    body.insert(body.end(), central.begin(), central.end());
    const size_t eocd = body.size();
    body.resize(eocd + 22);
    body[eocd + 0] = 'P'; body[eocd + 1] = 'K';
    body[eocd + 2] = 0x05; body[eocd + 3] = 0x06;
    wr_u16(body, eocd + 4, 0);             // 磁盘号
    wr_u16(body, eocd + 6, 0);             // 中央目录起始磁盘
    wr_u16(body, eocd + 8, static_cast<uint16_t>(entries.size()));
    wr_u16(body, eocd + 10, static_cast<uint16_t>(entries.size()));
    wr_u32(body, eocd + 12, central_size);
    wr_u32(body, eocd + 16, offset);
    wr_u16(body, eocd + 20, 0);            // 注释长度

    std::ofstream out(out_path, std::ios::binary);
    if (!out) return false;
    out.write(reinterpret_cast<const char*>(body.data()),
              static_cast<std::streamsize>(body.size()));
    return out.good();
}

// ===================== 旧版 .uplr 解析 =====================

// 与 Python 侧 SettingsManager._import_uplr_text 的字段映射保持一致
static const char* kStrKeys[] = {
    "project_name", "ust_path", "music_path", "song_name", "song_author",
    "ust_author", "encoding", "bg_color", "note_color", "lyric_color",
    "lyric_text_color", "other_text_color", "pitch_curve_color", "lyric_pos",
    "lrc_path", "silent_display", "silent_custom_text", "end_display",
    "end_custom_text", "pitch_placeholder", "pitch_custom_text",
};

static const char* kBoolKeys[] = {
    "show_bpm", "show_play_time", "show_song_name", "show_song_author",
    "show_ust_author", "fullscreen", "show_lyric", "curve_show",
    "show_phoneme", "show_midinote", "show_waveform",
};

static bool is_str_key(const std::string& k) {
    for (const char* s : kStrKeys) if (k == s) return true;
    return false;
}

static bool is_bool_key(const std::string& k) {
    for (const char* s : kBoolKeys) if (k == s) return true;
    return false;
}

static bool parse_bool(const std::string& v) {
    std::string t;
    for (char c : v) t += static_cast<char>(::tolower(static_cast<unsigned char>(c)));
    return t == "1" || t == "true" || t == "yes" || t == "on";
}

// 结果：field -> value（字符串字段原样；布尔字段 "0"/"1"）
static bool parse_old_uplr(const fs::path& path, std::map<std::string, std::string>& out) {
    std::ifstream in(path, std::ios::binary);
    if (!in) return false;
    std::string line;
    while (std::getline(in, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        // 去首尾空白
        size_t b = line.find_first_not_of(" \t");
        if (b == std::string::npos) continue;
        size_t e = line.find_last_not_of(" \t");
        line = line.substr(b, e - b + 1);
        if (line.empty() || line[0] == '#') continue;
        size_t eq = line.find('=');
        if (eq == std::string::npos) continue;
        std::string key = line.substr(0, eq);
        std::string value = line.substr(eq + 1);
        // 去 key 尾空白 / value 首尾空白
        key.erase(key.find_last_not_of(" \t") + 1);
        size_t vb = value.find_first_not_of(" \t");
        if (vb == std::string::npos) value.clear();
        else {
            size_t ve = value.find_last_not_of(" \t");
            value = value.substr(vb, ve - vb + 1);
        }
        if (is_str_key(key)) {
            out[key] = value;
        } else if (is_bool_key(key)) {
            out[key] = parse_bool(value) ? "1" : "0";
        }
    }
    return true;
}

// ===================== JSON 工具 =====================

static std::string json_escape(const std::string& s) {
    std::string r;
    r.reserve(s.size() + 8);
    for (unsigned char c : s) {
        switch (c) {
            case '"': r += "\\\""; break;
            case '\\': r += "\\\\"; break;
            case '\n': r += "\\n"; break;
            case '\r': r += "\\r"; break;
            case '\t': r += "\\t"; break;
            default:
                if (c < 0x20) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                    r += buf;
                } else {
                    r += static_cast<char>(c);
                }
        }
    }
    return r;
}

// 字段值 → JSON 字符串或 null
static std::string json_val(const std::map<std::string, std::string>& s,
                            const std::string& key) {
    auto it = s.find(key);
    if (it == s.end() || it->second.empty()) return "null";
    return "\"" + json_escape(it->second) + "\"";
}

static std::string json_bool(const std::map<std::string, std::string>& s,
                             const std::string& key, bool def) {
    auto it = s.find(key);
    if (it == s.end()) return def ? "1" : "0";
    return it->second == "1" ? "1" : "0";
}

// 与 Python 侧 _settings_to_info_json 结构一致（display 含 curve_show，color 含 pitch_curve_color）
static std::string build_info_json(const std::map<std::string, std::string>& s,
                                   const std::string& ust_name,
                                   const std::string& music_name,
                                   const std::string& lrc_name) {
    std::ostringstream j;
    j << "{\n";
    j << "    \"encoding\": " << json_val(s, "encoding") << ",\n";
    j << "    \"basic\": {\n";
    j << "        \"project_name\": " << json_val(s, "project_name") << ",\n";
    j << "        \"ust_path\": " << (ust_name.empty() ? "null" : "\"" + json_escape(ust_name) + "\"") << ",\n";
    j << "        \"music_path\": " << (music_name.empty() ? "null" : "\"" + json_escape(music_name) + "\"") << ",\n";
    j << "        \"song_name\": " << json_val(s, "song_name") << ",\n";
    j << "        \"song_author\": " << json_val(s, "song_author") << ",\n";
    j << "        \"ust_author\": " << json_val(s, "ust_author") << "\n";
    j << "    },\n";
    j << "    \"display\": {\n";
    j << "        \"show_bpm\": " << json_bool(s, "show_bpm", true) << ",\n";
    j << "        \"show_play_time\": " << json_bool(s, "show_play_time", true) << ",\n";
    j << "        \"show_song_name\": " << json_bool(s, "show_song_name", true) << ",\n";
    j << "        \"show_song_author\": " << json_bool(s, "show_song_author", true) << ",\n";
    j << "        \"show_ust_author\": " << json_bool(s, "show_ust_author", true) << ",\n";
    j << "        \"show_phoneme\": " << json_bool(s, "show_phoneme", false) << ",\n";
    j << "        \"show_midinote\": " << json_bool(s, "show_midinote", false) << ",\n";
    j << "        \"show_waveform\": " << json_bool(s, "show_waveform", false) << ",\n";
    j << "        \"fullscreen\": " << json_bool(s, "fullscreen", true) << ",\n";
    j << "        \"show_lyric\": " << json_bool(s, "show_lyric", false) << ",\n";
    j << "        \"curve_show\": " << json_bool(s, "curve_show", false) << "\n";
    j << "    },\n";
    j << "    \"color\": {\n";
    j << "        \"bg_color\": " << json_val(s, "bg_color") << ",\n";
    j << "        \"note_color\": " << json_val(s, "note_color") << ",\n";
    j << "        \"lyric_color\": " << json_val(s, "lyric_color") << ",\n";
    j << "        \"lyric_text_color\": " << json_val(s, "lyric_text_color") << ",\n";
    j << "        \"other_text_color\": " << json_val(s, "other_text_color") << ",\n";
    j << "        \"pitch_curve_color\": " << json_val(s, "pitch_curve_color") << "\n";
    j << "    },\n";
    j << "    \"else\": {\n";
    j << "        \"lyric_pos\": " << json_val(s, "lyric_pos") << ",\n";
    j << "        \"lrc_path\": " << (lrc_name.empty() ? "null" : "\"" + json_escape(lrc_name) + "\"") << ",\n";
    j << "        \"silent_display\": " << json_val(s, "silent_display") << ",\n";
    j << "        \"silent_custom_text\": " << json_val(s, "silent_custom_text") << ",\n";
    j << "        \"end_display\": " << json_val(s, "end_display") << ",\n";
    j << "        \"end_custom_text\": " << json_val(s, "end_custom_text") << ",\n";
    j << "        \"pitch_placeholder\": " << json_val(s, "pitch_placeholder") << ",\n";
    j << "        \"pitch_custom_text\": " << json_val(s, "pitch_custom_text") << "\n";
    j << "    }\n";
    j << "}";
    return j.str();
}

// ===================== 资源读取 =====================

static bool read_file(const fs::path& path, std::vector<uint8_t>& out) {
    std::ifstream in(path, std::ios::binary | std::ios::ate);
    if (!in) return false;
    std::streamsize n = in.tellg();
    if (n < 0) return false;
    in.seekg(0);
    out.resize(static_cast<size_t>(n));
    in.read(reinterpret_cast<char*>(out.data()), n);
    return in.good() || in.eof();
}

// 解析资源路径：相对路径以 base 目录为准，并返回包内文件名（basename）
static bool collect_resource(const fs::path& base, const std::string& raw,
                             std::string& out_name, std::vector<uint8_t>& out_data) {
    if (raw.empty()) return false;
    fs::path p(raw);
    if (p.is_relative()) p = base / p;
    std::error_code ec;
    if (!fs::exists(p, ec)) return false;
    if (!read_file(p, out_data)) return false;
    out_name = p.filename().string();
    return true;
}

// ===================== 转换入口（命令行与 TUI 共用） =====================
// 返回值：0=成功，1=读取失败，2=写入失败

static int convert_file(const fs::path& input, const fs::path& output) {
    std::map<std::string, std::string> settings;
    if (!parse_old_uplr(input, settings)) return 1;

    const fs::path base = input.parent_path().empty() ? fs::current_path() : input.parent_path();

    // ---- 收集资源 ----
    std::vector<ZipEntry> entries;
    std::string ust_name, music_name, lrc_name;

    std::string name;
    std::vector<uint8_t> data;
    if (collect_resource(base, settings["ust_path"], name, data)) {
        ust_name = name;
        entries.push_back({name, std::move(data)});
    }
    if (collect_resource(base, settings["music_path"], name, data)) {
        music_name = name;
        entries.push_back({name, std::move(data)});
    }
    if (collect_resource(base, settings["lrc_path"], name, data)) {
        lrc_name = name;
        entries.push_back({name, std::move(data)});
    }

    // ---- Info.json ----
    const std::string info_json = build_info_json(settings, ust_name, music_name, lrc_name);
    entries.insert(entries.begin(), {"Info.json",
        std::vector<uint8_t>(info_json.begin(), info_json.end())});

    if (!write_zip(output, entries)) return 2;
    return 0;
}

// ===================== TUI（鼠标交互菜单） =====================

class Tui {
public:
    void run() {
        h_in_ = GetStdHandle(STD_INPUT_HANDLE);
        h_out_ = GetStdHandle(STD_OUTPUT_HANDLE);
        if (h_in_ == INVALID_HANDLE_VALUE || h_out_ == INVALID_HANDLE_VALUE) {
            std::cerr << "无法获取控制台句柄\n";
            return;
        }
        DWORD old_in = 0, old_out = 0;
        GetConsoleMode(h_in_, &old_in);
        GetConsoleMode(h_out_, &old_out);

        // 启用鼠标输入；输出启用 ANSI VT 转义
        SetConsoleMode(h_in_, ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS | ENABLE_PROCESSED_INPUT);
        SetConsoleMode(h_out_, old_out | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        SetConsoleOutputCP(CP_UTF8);

        running_ = true;
        while (running_) {
            render();
            INPUT_RECORD rec{};
            DWORD n = 0;
            if (!ReadConsoleInput(h_in_, &rec, 1, &n)) break;
            if (rec.EventType == KEY_EVENT && rec.Event.KeyEvent.bKeyDown) {
                handle_key(rec.Event.KeyEvent);
            } else if (rec.EventType == MOUSE_EVENT) {
                handle_mouse(rec.Event.MouseEvent);
            }
        }

        // 恢复控制台模式
        SetConsoleMode(h_in_, old_in);
        SetConsoleMode(h_out_, old_out);
        write_out("\x1b[0m");
    }

private:
    static constexpr int kItemCount = 4;
    static constexpr char kKeys[kItemCount] = {'1', '2', '3', '4'};
    static constexpr const char* kLabels[kItemCount] = {
        "选择输入 .uplr 文件",
        "设置输出路径",
        "开始转换",
        "退出",
    };
    // 菜单项所在行（0-based，与 render 布局一致）
    static constexpr int kMenuRow[kItemCount] = {6, 7, 8, 9};

    HANDLE h_in_ = INVALID_HANDLE_VALUE;
    HANDLE h_out_ = INVALID_HANDLE_VALUE;
    bool running_ = false;
    int cursor_ = 0;
    std::string input_;
    std::string output_;
    std::string status_;

    void write_out(const std::string& s) {
        DWORD n = 0;
        WriteConsoleA(h_out_, s.c_str(), static_cast<DWORD>(s.size()), &n, nullptr);
    }

    static std::string wstr_to_utf8(const std::wstring& w) {
        if (w.empty()) return {};
        int len = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()),
                                      nullptr, 0, nullptr, nullptr);
        if (len <= 0) return {};
        std::string s(static_cast<size_t>(len), '\0');
        WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()),
                            &s[0], len, nullptr, nullptr);
        return s;
    }

    void render() {
        std::string out;
        out += "\x1b[2J\x1b[H";  // 清屏 + 光标归位
        out += "\x1b[36m ═══════════════════════════════════════════════════\r\n";
        out += "   ustPlayer .uplr 转换器（旧版文本 → 新版 ZIP）\r\n";
        out += " ═══════════════════════════════════════════════════\x1b[0m\r\n";
        out += " 输入文件: " + (input_.empty() ? "\x1b[33m（未选择）\x1b[0m" : input_) + "\r\n";
        out += " 输出文件: " + (output_.empty() ? "\x1b[33m（自动生成）\x1b[0m" : output_) + "\r\n";
        out += " ----------------------------------------------\r\n";
        for (int i = 0; i < kItemCount; i++) {
            if (i == cursor_) out += "\x1b[7m";
            out += " [" + std::string(1, kKeys[i]) + "] " + kLabels[i];
            if (i == cursor_) out += "\x1b[0m";
            out += "\r\n";
        }
        out += " ----------------------------------------------\r\n";
        out += " 提示: 鼠标单击菜单项 / 数字键 / 方向键+回车\r\n";
        out += status_ + "\r\n";
        write_out(out);
    }

    void activate(int i) {
        switch (i) {
            case 0: pick_input(); break;
            case 1: pick_output(); break;
            case 2: do_convert(); break;
            case 3: running_ = false; break;
            default: break;
        }
    }

    void handle_key(KEY_EVENT_RECORD& e) {
        if (e.wVirtualKeyCode == VK_UP) {
            cursor_ = (cursor_ + kItemCount - 1) % kItemCount;
        } else if (e.wVirtualKeyCode == VK_DOWN) {
            cursor_ = (cursor_ + 1) % kItemCount;
        } else if (e.wVirtualKeyCode == VK_RETURN) {
            activate(cursor_);
        } else {
            const char c = static_cast<char>(e.uChar.AsciiChar);
            for (int i = 0; i < kItemCount; i++) {
                if (c == kKeys[i]) { activate(i); return; }
            }
        }
    }

    void handle_mouse(MOUSE_EVENT_RECORD& e) {
        if (!(e.dwButtonState & FROM_LEFT_1ST_BUTTON_PRESSED)) return;
        const short y = e.dwMousePosition.Y;
        for (int i = 0; i < kItemCount; i++) {
            if (y == kMenuRow[i]) { activate(i); return; }
        }
    }

    // 读取一行宽字符输入 → UTF-8；空输入表示取消
    std::string read_path(const std::string& prompt) {
        write_out("\r\n" + prompt + "\r\n> ");
        DWORD old_in = 0;
        GetConsoleMode(h_in_, &old_in);
        // 切到行输入（回显），读取期间不响应鼠标
        SetConsoleMode(h_in_, ENABLE_PROCESSED_INPUT | ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT);
        wchar_t buf[4096] = {};
        DWORD n = 0;
        BOOL ok = ReadConsoleW(h_in_, buf, 4095, &n, nullptr);
        SetConsoleMode(h_in_, ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS | ENABLE_PROCESSED_INPUT);
        if (!ok) return {};
        size_t len = n;
        while (len > 0 && (buf[len - 1] == L'\r' || buf[len - 1] == L'\n')) len--;
        std::wstring w(buf, len);
        size_t b = w.find_first_not_of(L" \t");
        size_t e = w.find_last_not_of(L" \t");
        if (b == std::wstring::npos) return {};
        w = w.substr(b, e - b + 1);
        return wstr_to_utf8(w);
    }

    void pick_input() {
        input_.clear();
        const std::string p = read_path("请输入旧版 .uplr 文件路径（回车取消）:");
        if (p.empty()) {
            status_ = "\x1b[33m已取消选择\x1b[0m";
            return;
        }
        if (!fs::exists(fs::u8path(p))) {
            status_ = "\x1b[31m文件不存在: " + p + "\x1b[0m";
            return;
        }
        input_ = p;
        // 自动生成默认输出路径：同目录 <原名>_new.uplr
        fs::path ip = fs::u8path(p);
        output_ = (ip.parent_path() / (ip.stem().string() + "_new.uplr")).string();
        status_ = "\x1b[32m已选择输入文件\x1b[0m";
    }

    void pick_output() {
        if (input_.empty()) {
            status_ = "\x1b[33m请先选择输入文件\x1b[0m";
            return;
        }
        const std::string p = read_path("请输入输出 .uplr 路径（回车保留默认）:");
        if (!p.empty()) {
            output_ = p;
            status_ = "\x1b[32m已设置输出路径\x1b[0m";
        } else {
            status_ = "\x1b[33m保留默认输出路径\x1b[0m";
        }
    }

    void do_convert() {
        if (input_.empty()) {
            status_ = "\x1b[31m请先选择输入文件\x1b[0m";
            return;
        }
        if (output_.empty()) {
            fs::path ip = fs::u8path(input_);
            output_ = (ip.parent_path() / (ip.stem().string() + "_new.uplr")).string();
        }
        write_out("\x1b[2J\x1b[H\x1b[36m 正在转换...\x1b[0m\r\n");
        const int rc = convert_file(fs::u8path(input_), fs::u8path(output_));
        if (rc == 0) {
            status_ = "\x1b[32m转换完成: " + output_ + "\x1b[0m";
        } else if (rc == 1) {
            status_ = "\x1b[31m读取旧版工程失败: " + input_ + "\x1b[0m";
        } else {
            status_ = "\x1b[31m写入新版工程失败: " + output_ + "\x1b[0m";
        }
    }
};

// ===================== main =====================

int main(int argc, char** argv) {
    // 统一 UTF-8 输出代码页（Windows 10+ 控制台可正确显示中文）
    SetConsoleOutputCP(CP_UTF8);

    if (argc >= 3) {
        // 命令行模式：uplr_converter.exe <input.uplr> <output.uplr>
        const fs::path input = fs::u8path(argv[1]);
        const fs::path output = fs::u8path(argv[2]);
        const int rc = convert_file(input, output);
        if (rc == 0) {
            std::cout << "转换完成: " << output.string() << "\n";
        } else if (rc == 1) {
            std::cerr << "无法读取旧版工程文件: " << input.string() << "\n";
        } else {
            std::cerr << "无法写入新版工程文件: " << output.string() << "\n";
        }
        return rc;
    }

    // TUI 模式（默认）：鼠标可点击的交互菜单
    Tui tui;
    tui.run();
    return 0;
}
