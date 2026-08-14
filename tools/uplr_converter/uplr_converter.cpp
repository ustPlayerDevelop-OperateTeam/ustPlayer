// uplr_converter.cpp — 旧版 .uplr（纯文本）→ 新版 .uplr（ZIP 容器）转换器
//
// 零第三方依赖，C++17。
// ZIP 使用 STORE（不压缩）写入，UTF-8 文件名（bit 11 置位）。
//
// 用法: uplr_converter.exe <input.uplr> <output.uplr>
//   input.uplr  旧版纯文本 .uplr（UTF-8，key=value）
//   output.uplr 新版 ZIP .uplr（内含 Info.json + 资源文件）
//
// 资源文件（ust/lrc/music）按旧文件中的路径解析（相对路径相对于 input 所在目录），
// 存在才打包；Info.json 中缺失资源对应 null。旧格式没有 music_path → 恒为 null。

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

// ===================== main =====================

int main(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "用法: uplr_converter.exe <input.uplr> <output.uplr>\n";
        return 2;
    }
    const fs::path input = fs::u8path(argv[1]);
    const fs::path output = fs::u8path(argv[2]);

    std::map<std::string, std::string> settings;
    if (!parse_old_uplr(input, settings)) {
        std::cerr << "无法读取旧版工程文件: " << input.string() << "\n";
        return 1;
    }

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

    if (!write_zip(output, entries)) {
        std::cerr << "无法写入新版工程文件: " << output.string() << "\n";
        return 1;
    }

    std::cout << "转换完成: " << output.string()
              << " (资源 " << entries.size() - 1 << " 个)\n";
    return 0;
}
