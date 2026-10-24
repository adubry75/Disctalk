using System.Text;
using System.Text.RegularExpressions;
using DisctalkClient.Core.Models;

namespace DisctalkClient.Core.Sending;

public sealed record UnicodeEmoji(string Name, string Value, string Category);

/// <summary>
/// Turns <c>:name:</c> in outgoing text into the real thing, like the official client does before sending:
/// server emoji become <c>&lt;:name:id&gt;</c>, common shortcodes become unicode. Code spans are left alone.
/// </summary>
public static partial class EmojiShortcodes
{
    [GeneratedRegex(@"(?<![<\w]):([A-Za-z0-9_+\-]{2,32}):(?!\d)")]
    private static partial Regex Shortcode();

    [GeneratedRegex(@"```[\s\S]*?```|`[^`\n]+`|<a?:\w+:\d+>")]
    private static partial Regex Protected();

    public static string Expand(string text, IEnumerable<GuildEmojiInfo> guildEmojis)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(':')) return text;
        var custom = new Dictionary<string, GuildEmojiInfo>(StringComparer.Ordinal);
        foreach (var e in guildEmojis) custom.TryAdd(e.Name, e);

        var sb = new StringBuilder(text.Length);
        int pos = 0;
        foreach (Match p in Protected().Matches(text))
        {
            sb.Append(ExpandSegment(text[pos..p.Index], custom));
            sb.Append(p.Value);
            pos = p.Index + p.Length;
        }
        sb.Append(ExpandSegment(text[pos..], custom));
        return sb.ToString();
    }

    static string ExpandSegment(string segment, Dictionary<string, GuildEmojiInfo> custom) =>
        Shortcode().Replace(segment, m =>
        {
            var name = m.Groups[1].Value;
            if (custom.TryGetValue(name, out var e)) return e.Animated ? $"<a:{e.Name}:{e.Id}>" : $"<:{e.Name}:{e.Id}>";
            if (ByName.TryGetValue(name, out var u)) return u.Value;
            return m.Value;
        });

    public static IReadOnlyList<UnicodeEmoji> Common { get; } =
    [
        new("smile", "😄", "People"), new("grin", "😁", "People"), new("joy", "😂", "People"), new("rofl", "🤣", "People"),
        new("smiley", "😃", "People"), new("sweat_smile", "😅", "People"), new("wink", "😉", "People"), new("blush", "😊", "People"),
        new("innocent", "😇", "People"), new("heart_eyes", "😍", "People"), new("star_struck", "🤩", "People"), new("kissing_heart", "😘", "People"),
        new("yum", "😋", "People"), new("stuck_out_tongue", "😛", "People"), new("zany_face", "🤪", "People"), new("thinking", "🤔", "People"),
        new("neutral_face", "😐", "People"), new("expressionless", "😑", "People"), new("smirk", "😏", "People"), new("unamused", "😒", "People"),
        new("rolling_eyes", "🙄", "People"), new("grimacing", "😬", "People"), new("relieved", "😌", "People"), new("pensive", "😔", "People"),
        new("sleepy", "😪", "People"), new("sleeping", "😴", "People"), new("mask", "😷", "People"), new("nerd", "🤓", "People"),
        new("sunglasses", "😎", "People"), new("confused", "😕", "People"), new("worried", "😟", "People"), new("open_mouth", "😮", "People"),
        new("flushed", "😳", "People"), new("pleading_face", "🥺", "People"), new("cry", "😢", "People"), new("sob", "😭", "People"),
        new("scream", "😱", "People"), new("angry", "😠", "People"), new("rage", "😡", "People"), new("skull", "💀", "People"),
        new("clown", "🤡", "People"), new("ghost", "👻", "People"), new("alien", "👽", "People"), new("robot", "🤖", "People"),
        new("poop", "💩", "People"), new("wave", "👋", "People"), new("ok_hand", "👌", "People"), new("v", "✌️", "People"),
        new("crossed_fingers", "🤞", "People"), new("thumbsup", "👍", "People"), new("+1", "👍", "People"), new("thumbsdown", "👎", "People"),
        new("-1", "👎", "People"), new("clap", "👏", "People"), new("raised_hands", "🙌", "People"), new("pray", "🙏", "People"),
        new("muscle", "💪", "People"), new("eyes", "👀", "People"), new("brain", "🧠", "People"), new("salute", "🫡", "People"),
        new("shrug", "🤷", "People"), new("facepalm", "🤦", "People"), new("point_up", "☝️", "People"), new("handshake", "🤝", "People"),

        new("cat", "🐱", "Cats"), new("cat2", "🐈", "Cats"), new("black_cat", "🐈‍⬛", "Cats"), new("smiley_cat", "😺", "Cats"),
        new("smile_cat", "😸", "Cats"), new("joy_cat", "😹", "Cats"), new("heart_eyes_cat", "😻", "Cats"), new("smirk_cat", "😼", "Cats"),
        new("kissing_cat", "😽", "Cats"), new("scream_cat", "🙀", "Cats"), new("crying_cat_face", "😿", "Cats"), new("pouting_cat", "😾", "Cats"),
        new("lion", "🦁", "Cats"), new("tiger", "🐯", "Cats"), new("paw_prints", "🐾", "Cats"), new("fish", "🐟", "Cats"),
        new("yarn", "🧶", "Cats"), new("mouse", "🐭", "Cats"), new("dog", "🐶", "Nature"), new("fox", "🦊", "Nature"),
        new("bear", "🐻", "Nature"), new("panda_face", "🐼", "Nature"), new("frog", "🐸", "Nature"), new("penguin", "🐧", "Nature"),
        new("bird", "🐦", "Nature"), new("owl", "🦉", "Nature"), new("bee", "🐝", "Nature"), new("butterfly", "🦋", "Nature"),
        new("snake", "🐍", "Nature"), new("turtle", "🐢", "Nature"), new("octopus", "🐙", "Nature"), new("crab", "🦀", "Nature"),
        new("cherry_blossom", "🌸", "Nature"), new("rose", "🌹", "Nature"), new("sunflower", "🌻", "Nature"), new("seedling", "🌱", "Nature"),
        new("evergreen_tree", "🌲", "Nature"), new("cactus", "🌵", "Nature"), new("sunny", "☀️", "Nature"), new("rainbow", "🌈", "Nature"),
        new("snowflake", "❄️", "Nature"), new("zap", "⚡", "Nature"), new("fire", "🔥", "Nature"), new("droplet", "💧", "Nature"),
        new("star", "⭐", "Nature"), new("sparkles", "✨", "Nature"), new("crescent_moon", "🌙", "Nature"), new("earth_americas", "🌎", "Nature"),

        new("pizza", "🍕", "Food"), new("hamburger", "🍔", "Food"), new("fries", "🍟", "Food"), new("taco", "🌮", "Food"),
        new("sushi", "🍣", "Food"), new("ramen", "🍜", "Food"), new("cookie", "🍪", "Food"), new("cake", "🍰", "Food"),
        new("doughnut", "🍩", "Food"), new("icecream", "🍦", "Food"), new("apple", "🍎", "Food"), new("banana", "🍌", "Food"),
        new("avocado", "🥑", "Food"), new("popcorn", "🍿", "Food"), new("coffee", "☕", "Food"), new("tea", "🍵", "Food"),
        new("beer", "🍺", "Food"), new("wine_glass", "🍷", "Food"), new("tada", "🎉", "Objects"), new("confetti_ball", "🎊", "Objects"),
        new("balloon", "🎈", "Objects"), new("gift", "🎁", "Objects"), new("trophy", "🏆", "Objects"), new("medal", "🏅", "Objects"),
        new("video_game", "🎮", "Objects"), new("game_die", "🎲", "Objects"), new("musical_note", "🎵", "Objects"), new("headphones", "🎧", "Objects"),
        new("computer", "💻", "Objects"), new("keyboard", "⌨️", "Objects"), new("iphone", "📱", "Objects"), new("camera", "📷", "Objects"),
        new("bulb", "💡", "Objects"), new("books", "📚", "Objects"), new("pencil", "✏️", "Objects"), new("memo", "📝", "Objects"),
        new("pushpin", "📌", "Objects"), new("lock", "🔒", "Objects"), new("key", "🔑", "Objects"), new("hammer", "🔨", "Objects"),
        new("wrench", "🔧", "Objects"), new("gear", "⚙️", "Objects"), new("rocket", "🚀", "Objects"), new("hourglass", "⌛", "Objects"),
        new("bell", "🔔", "Objects"), new("moneybag", "💰", "Objects"), new("crown", "👑", "Objects"), new("gem", "💎", "Objects"),

        new("heart", "❤️", "Symbols"), new("orange_heart", "🧡", "Symbols"), new("yellow_heart", "💛", "Symbols"), new("green_heart", "💚", "Symbols"),
        new("blue_heart", "💙", "Symbols"), new("purple_heart", "💜", "Symbols"), new("black_heart", "🖤", "Symbols"), new("white_heart", "🤍", "Symbols"),
        new("broken_heart", "💔", "Symbols"), new("two_hearts", "💕", "Symbols"), new("sparkling_heart", "💖", "Symbols"), new("100", "💯", "Symbols"),
        new("white_check_mark", "✅", "Symbols"), new("heavy_check_mark", "✔️", "Symbols"), new("x", "❌", "Symbols"), new("warning", "⚠️", "Symbols"),
        new("no_entry", "⛔", "Symbols"), new("question", "❓", "Symbols"), new("exclamation", "❗", "Symbols"), new("bangbang", "‼️", "Symbols"),
        new("zzz", "💤", "Symbols"), new("boom", "💥", "Symbols"), new("speech_balloon", "💬", "Symbols"), new("thought_balloon", "💭", "Symbols"),
        new("arrow_up", "⬆️", "Symbols"), new("arrow_down", "⬇️", "Symbols"), new("arrow_right", "➡️", "Symbols"), new("arrow_left", "⬅️", "Symbols"),
        new("recycle", "♻️", "Symbols"), new("infinity", "♾️", "Symbols"), new("new", "🆕", "Symbols"), new("ok", "🆗", "Symbols"),
    ];

    static readonly Dictionary<string, UnicodeEmoji> ByName = Common.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    public static UnicodeEmoji? Find(string name) => ByName.GetValueOrDefault(name);
}
