using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace AlchemyStars.Avalonia;

/// <summary>Original 12×12 pixel-grid artwork, rendered as integer-aligned vector blocks at 24 units.</summary>
internal static class Windows2000Icons
{
    private static readonly IReadOnlyDictionary<char, IBrush> Palette = new Dictionary<char, IBrush>
    {
        ['b'] = new SolidColorBrush(Color.Parse("#000080")),
        ['c'] = new SolidColorBrush(Color.Parse("#00b8c8")),
        ['g'] = new SolidColorBrush(Color.Parse("#008000")),
        ['r'] = new SolidColorBrush(Color.Parse("#c00000")),
        ['y'] = new SolidColorBrush(Color.Parse("#ffff80")),
        ['w'] = new SolidColorBrush(Color.Parse("#ffffff")),
        ['s'] = new SolidColorBrush(Color.Parse("#c0c0c0")),
        ['k'] = new SolidColorBrush(Color.Parse("#000000")),
    };

    public static Canvas Create(string glyph)
    {
        var canvas = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false, UseLayoutRounding = true };
        void Block(int x, int y, int width, int height, char color)
        {
            var block = new Rectangle { Width = width * 2, Height = height * 2, Fill = Palette[color], IsHitTestVisible = false };
            Canvas.SetLeft(block, x * 2);
            Canvas.SetTop(block, y * 2);
            canvas.Children.Add(block);
        }
        void Pixels(string rows, int x = 0, int y = 0)
        {
            foreach (var row in rows.Split('/'))
            {
                for (var i = 0; i < row.Length; i++)
                    if (Palette.ContainsKey(row[i])) Block(x + i, y, 1, 1, row[i]);
                y++;
            }
        }
        void Panel()
        {
            Block(1, 1, 10, 10, 'k'); Block(2, 2, 8, 8, 'w'); Block(2, 2, 8, 2, 'b');
            Block(8, 2, 1, 1, 'r');
        }
        void Folder()
        {
            Pixels(".yyyy......./.ywwy......./.yyyyyyyyy../.ywwwwwwwy../.yyyyyyyyyy./.yyyyyyyywy./.yyyyyyyywy./.yyyyyyyywy./.yyyyyyyyyy.", 0, 2);
        }
        void Disk()
        {
            Block(1, 1, 9, 10, 'b'); Block(10, 2, 1, 9, 'b');
            Block(3, 1, 5, 4, 's'); Block(6, 2, 1, 2, 'k');
            Block(3, 7, 6, 4, 'w'); Block(4, 8, 4, 1, 'c'); Block(4, 10, 3, 1, 'c');
        }
        void Arrow(bool up)
        {
            Pixels(up ? ".....gg...../....gggg..../...gggggg.../..gggggggg../.....gg...../.....gg...../.....gg...../.....gg....." : ".....gg...../.....gg...../.....gg...../.....gg...../..gggggggg../...gggggg.../....gggg..../.....gg.....", 0, 2);
        }
        switch (glyph)
        {
            case "about":
                Pixels("...bbbbbb.../..bbccccbb../.bbcwwcccb../.bbccccccb../.bbcwccccb../.bbcwccccb../.bbcwccccb../.bbcwccccb../..bbbbbbbb../...bbbbbb...", 0, 1); break;
            case "notification":
                Pixels(".....yy...../....yyyy..../....ykky..../...yykkyy.../...yykkyy.../..yyykkyyy../..yyyyyyyy../.yyyykkyyyy./.yyyyyyyyyy.", 0, 1); break;
            case "add":
                Block(4, 1, 4, 10, 'g'); Block(1, 4, 10, 4, 'g'); Block(5, 2, 1, 3, 'w'); Block(2, 5, 3, 1, 'w'); break;
            case "animation-library":
                Block(1, 1, 10, 10, 'b'); Block(3, 2, 6, 8, 'k');
                foreach (var y in new[] { 2, 5, 8 }) { Block(1, y, 1, 1, 'w'); Block(10, y, 1, 1, 'w'); }
                Pixels("g.../gg../ggg./gg../g...", 4, 3); break;
            case "animation-layers":
                Block(1, 2, 8, 2, 'b'); Block(3, 5, 8, 2, 'g'); Block(1, 8, 7, 2, 'y');
                Block(2, 2, 1, 1, 'c'); Block(4, 5, 1, 1, 'w'); Block(2, 8, 1, 1, 'w'); break;
            case "batch-processing":
                Block(1, 1, 4, 4, 'b'); Block(7, 1, 4, 4, 'g'); Block(1, 7, 4, 4, 'y'); Block(7, 7, 4, 4, 'r');
                foreach (var (x,y) in new[] { (2,2), (8,2), (2,8), (8,8) }) Block(x, y, 1, 1, 'w'); break;
            case "camera-view":
                Block(1, 4, 10, 6, 's'); Block(3, 2, 5, 2, 's'); Block(4, 4, 5, 5, 'k'); Block(5, 5, 3, 3, 'b'); Block(5, 5, 1, 1, 'w'); Block(2, 5, 1, 1, 'y'); break;
            case "cast-preview":
                Block(1, 1, 10, 8, 's'); Block(2, 2, 8, 6, 'k'); Block(5, 9, 2, 1, 's'); Block(3, 10, 6, 1, 's'); Pixels("g.../gg../ggg./gg../g...", 4, 3); break;
            case "delete":
                Block(3, 4, 6, 7, 's'); Block(2, 2, 8, 2, 'r'); Block(4, 1, 4, 1, 'r'); Block(4, 5, 1, 5, 'k'); Block(7, 5, 1, 5, 'k'); break;
            case "dual-wield":
                Pixels(".cc....ss.../.bc....ks.../.bc....ks.../.bc....ks.../.bc....ks.../yyyy..yyyy../..b....k..../..b....k....", 1, 2); break;
            case "export-animation":
                Arrow(false); Block(1, 9, 2, 2, 's'); Block(9, 9, 2, 2, 's'); Block(1, 11, 10, 1, 's'); break;
            case "fit-view":
                Pixels("bbbb..bbbb../b........b../b........b../b..cccc..b../...cccc...../...cccc...../b..cccc..b../b........b../b........b../bbbb..bbbb..", 1, 1); break;
            case "hand-pose":
                Pixels("....yy....../..yyyyyy.../..yyyyyyy../..yyyyyyy../..yyyyyyy../yyyyyyyyy../yyyyyyyyy../.yyyyyyyy../..yyyyyy.../...bbbb....", 0, 1); break;
            case "import-assets": Folder(); Pixels("..gg../..gg../..gg../gggggg/.gggg./..gg..", 5, 0); break;
            case "inverse-kinematics":
                Block(2, 3, 2, 3, 's'); Block(4, 5, 3, 2, 's'); Block(7, 7, 2, 3, 's');
                Block(1, 1, 3, 3, 'b'); Block(4, 4, 3, 3, 'y'); Block(8, 8, 3, 3, 'g'); break;
            case "language":
                Pixels("...bbbbbb.../..bbcccbbb../.bbbggggcbb./.bbgggccbbb./.bbgcccccbb./.bbccgggcbb./.bbcccggcbb./.bbcccggbbb./..bbcccbbb../...bbbbbb...", 0, 1); break;
            case "model-parts":
                Pixels("..yyyy....../.yywwyy...../yyyyyyyy..../yyyyyyyy..../yyyyyyyy..../.yyyyyy...../.....bbbb.../....bbccbb../...bbbbbbbb./...bbbbbbbb./....bbbbbb.."); break;
            case "move-up": Arrow(true); break;
            case "move-down": Arrow(false); break;
            case "play": Pixels("g......./gg....../ggg...../gggg..../ggggg.../gggggg../ggggg.../gggg..../ggg...../gg....../g.......", 3, 1); break;
            case "pause": Block(2, 1, 3, 10, 'b'); Block(7, 1, 3, 10, 'b'); Block(2, 1, 1, 9, 'c'); Block(7, 1, 1, 9, 'c'); break;
            case "next-frame": Pixels("b.....bb/bb....bb/bbb...bb/bbbb..bb/bbbbb.bb/bbbb..bb/bbb...bb/bb....bb/b.....bb", 2, 2); break;
            case "previous-frame": Pixels("bb.....b/bb....bb/bb...bbb/bb..bbbb/bb.bbbbb/bb..bbbb/bb...bbb/bb....bb/bb.....b", 2, 2); break;
            case "output-naming":
                Panel(); Pixels("......rr/....rry/....yyy./...yyy../..yyy.../.yyy..../kkk.....", 2, 4); break;
            case "output-settings":
                Panel(); Block(3, 5, 1, 5, 's'); Block(6, 5, 1, 5, 's'); Block(8, 5, 1, 5, 's'); Block(2, 6, 3, 2, 'g'); Block(5, 8, 3, 2, 'b'); Block(8, 5, 2, 2, 'r'); break;
            case "project-workspace": Folder(); Block(5, 5, 6, 6, 'w'); Block(5, 5, 6, 2, 'b'); Block(6, 8, 4, 1, 's'); break;
            case "restore-layout":
                Pixels("..gggggg..../.gg....gg.../gg......gg../gg......gg../gg........../gggggg....../ggggg......./........gg../.gg....gg.../..gggggg....", 1, 1); break;
            case "save": Disk(); break;
            case "save-as": Disk(); Pixels("....yy/...yyy/..yyy./.yyy../yyy.../kk....", 6, 6); break;
            case "timeline-playback":
                Panel(); Block(3, 5, 3, 2, 'y'); Block(3, 8, 5, 2, 'b'); Pixels("g.../gg../ggg./gg../g...", 7, 5); break;
            case "weapon-follow":
                Block(1, 1, 4, 4, 'b'); Pixels("ggg...../..gg..../..gg..../..gg..../..gg..g./...gggg./....ggg./......g.", 3, 3); break;
            case "weapon-processing-mode":
                Block(5, 4, 2, 2, 's'); Block(2, 6, 8, 1, 's'); Block(2, 6, 1, 3, 's'); Block(9, 6, 1, 3, 's'); Block(4, 1, 4, 3, 'b'); Block(1, 8, 4, 3, 'y'); Block(7, 8, 4, 3, 'g'); break;
            case "zoom-in":
            case "zoom-out":
                Pixels("..bbbbb...../.bbcccbb..../bbcccccbb.../bbcccccbb.../bbcccccbb.../.bbcccbb..../..bbbbb...../......yy..../.......yy.../........yy..", 1, 1);
                Block(4, 4, 3, 1, 'k'); if (glyph == "zoom-in") Block(5, 3, 1, 3, 'k'); break;
            default: throw new ArgumentOutOfRangeException(nameof(glyph), glyph, "Windows 2000 icon has not been drawn.");
        }
        return canvas;
    }
}
