using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SysFileAccess = System.IO.FileAccess;
using SysFileMode = System.IO.FileMode;
using SysFileStream = System.IO.FileStream;
using SysBinaryReader = System.IO.BinaryReader;

/// <summary>
/// Loads HoI4 rivers bitmap (rivers.bmp) for river network.
/// 5632×2048 8-bit BMP, non-zero values indicate river pixels.
/// </summary>
public static class RiversLoader
{
    private const string HoI4MapPath = @"C:\Proje\Hearts of Iron IV No Compromise No Surrender\map";
    private const string RiversBmpPath = HoI4MapPath + @"\rivers.bmp";

    public static int MapWidth { get; private set; } = 5632;
    public static int MapHeight { get; private set; } = 2048;

    // River data: 0 = no river, >0 = river (value may indicate size/type)
    private static byte[] _riverData;

    // River segments for rendering (connected river pixels)
    private static List<RiverSegment> _riverSegments = new();

    public class RiverSegment
    {
        public List<Vector2> Points { get; set; } = new();
        public byte RiverType { get; set; }
        public float Width { get; set; } = 1.0f;
    }

    /// <summary>
    /// Initialize rivers system
    /// </summary>
    public static async Task InitializeAsync()
    {
        GD.Print("[RiversLoader] Loading rivers.bmp...");

        if (!File.Exists(RiversBmpPath))
        {
            GD.PrintErr($"[RiversLoader] rivers.bmp not found at {RiversBmpPath}");
            return;
        }

        await LoadRiversAsync();
        ExtractRiverSegments();

        GD.Print($"[RiversLoader] Loaded: {MapWidth}x{MapHeight}, {_riverSegments.Count} river segments");
    }

    private static async Task LoadRiversAsync()
    {
        using (var fs = new SysFileStream(RiversBmpPath, SysFileMode.Open, SysFileAccess.Read))
        using (var br = new SysBinaryReader(fs))
        {
            var signature = br.ReadBytes(2);
            if (signature[0] != 'B' || signature[1] != 'M')
            {
                GD.PrintErr("[RiversLoader] Not a valid BMP file");
                return;
            }

            br.ReadBytes(4); br.ReadBytes(4);
            int dataOffset = br.ReadInt32();
            int headerSize = br.ReadInt32();
            MapWidth = br.ReadInt32();
            MapHeight = br.ReadInt32();
            br.ReadInt16();
            short bitsPerPixel = br.ReadInt16();
            br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4); br.ReadBytes(4);

            bool topDown = MapHeight < 0;
            MapHeight = Math.Abs(MapHeight);

            if (bitsPerPixel != 8)
            {
                GD.PrintErr($"[RiversLoader] Expected 8-bit BMP, got {bitsPerPixel}-bit");
                return;
            }

            int rowSize = ((MapWidth + 3) / 4) * 4;
            _riverData = new byte[MapWidth * MapHeight];

            fs.Seek(dataOffset, SeekOrigin.Begin);
            byte[] rowBuffer = new byte[rowSize];

            for (int y = 0; y < MapHeight; y++)
            {
                int bytesRead = fs.Read(rowBuffer, 0, rowSize);
                int targetY = topDown ? y : (MapHeight - 1 - y);
                int targetOffset = targetY * MapWidth;

                for (int x = 0; x < MapWidth; x++)
                {
                    _riverData[targetOffset + x] = rowBuffer[x];
                }

                if (y % 256 == 0)
                    await Task.Yield();
            }
        }

        int riverPixels = 0;
        foreach (byte v in _riverData) if (v > 0) riverPixels++;
        GD.Print($"[RiversLoader] River pixels: {riverPixels} ({riverPixels * 100.0 / _riverData.Length:F2}%)");
    }

    /// <summary>
    /// Extract connected river segments from pixel data
    /// </summary>
    private static void ExtractRiverSegments()
    {
        var visited = new bool[_riverData.Length];
        _riverSegments.Clear();

        for (int y = 0; y < MapHeight; y++)
        {
            for (int x = 0; x < MapWidth; x++)
            {
                int idx = y * MapWidth + x;
                if (_riverData[idx] > 0 && !visited[idx])
                {
                    var segment = TraceRiverSegment(x, y, visited);
                    if (segment.Points.Count >= 2)
                    {
                        segment.RiverType = _riverData[idx];
                        _riverSegments.Add(segment);
                    }
                }
            }
        }
    }

    private static RiverSegment TraceRiverSegment(int startX, int startY, bool[] visited)
    {
        var segment = new RiverSegment();
        var stack = new Stack<(int x, int y)>();
        stack.Push((startX, startY));

        byte riverType = _riverData[startY * MapWidth + startX];

        while (stack.Count > 0)
        {
            var (x, y) = stack.Pop();
            int idx = y * MapWidth + x;

            if (x < 0 || x >= MapWidth || y < 0 || y >= MapHeight) continue;
            if (visited[idx]) continue;
            if (_riverData[idx] == 0) continue;
            if (_riverData[idx] != riverType) continue; // Same river type

            visited[idx] = true;
            segment.Points.Add(new Vector2(x, y));

            // 8-connected neighbors
            int[] dx = { -1, -1, -1, 0, 0, 1, 1, 1 };
            int[] dy = { -1, 0, 1, -1, 1, -1, 0, 1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];
                int nIdx = ny * MapWidth + nx;
                if (nx >= 0 && nx < MapWidth && ny >= 0 && ny < MapHeight)
                {
                    if (!visited[nIdx] && _riverData[nIdx] == riverType)
                        stack.Push((nx, ny));
                }
            }
        }

        // Simplify segment (Douglas-Peucker or simple decimation)
        if (segment.Points.Count > 10)
        {
            var simplified = new List<Vector2>();
            float epsilon = 2.0f; // pixels
            SimplifyPolyline(segment.Points, simplified, epsilon);
            segment.Points = simplified;
        }

        return segment;
    }

    private static void SimplifyPolyline(List<Vector2> input, List<Vector2> output, float epsilon)
    {
        if (input.Count < 3)
        {
            output.AddRange(input);
            return;
        }

        output.Add(input[0]);
        for (int i = 1; i < input.Count - 1; i++)
        {
            Vector2 prev = output[output.Count - 1];
            Vector2 curr = input[i];
            Vector2 next = input[i + 1];

            // Check if current point is far enough from line prev-next
            float dist = DistancePointToLine(curr, prev, next);
            if (dist > epsilon)
                output.Add(curr);
        }
        output.Add(input[input.Count - 1]);
    }

    private static float DistancePointToLine(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        Vector2 ap = p - a;
        float abLenSq = ab.LengthSquared();

        if (abLenSq == 0) return ap.Length();

        float t = Mathf.Clamp(ap.Dot(ab) / abLenSq, 0, 1);
        Vector2 projection = a + ab * t;
        return (p - projection).Length();
    }

    /// <summary>
    /// Check if there's a river at world position
    /// </summary>
    public static bool HasRiver(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 0, MapWidth - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, MapHeight - 1);
        return _riverData[y * MapWidth + x] > 0;
    }

    /// <summary>
    /// Get river type at position (0 = none)
    /// </summary>
    public static byte GetRiverType(Vector2 worldPos)
    {
        int x = Mathf.Clamp((int)worldPos.X, 0, MapWidth - 1);
        int y = Mathf.Clamp((int)worldPos.Y, 0, MapHeight - 1);
        return _riverData[y * MapWidth + x];
    }

    /// <summary>
    /// Get all river segments for rendering
    /// </summary>
    public static IReadOnlyList<RiverSegment> GetRiverSegments()
    {
        return _riverSegments;
    }

    /// <summary>
    /// Get river segments within a bounding box (for viewport culling)
    /// </summary>
    public static List<RiverSegment> GetRiverSegmentsInBounds(Rect2 bounds)
    {
        var result = new List<RiverSegment>();
        foreach (var seg in _riverSegments)
        {
            if (seg.Points.Count == 0) continue;

            // Quick bbox check
            float minX = seg.Points[0].X, maxX = seg.Points[0].X;
            float minY = seg.Points[0].Y, maxY = seg.Points[0].Y;
            foreach (var p in seg.Points)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            if (maxX >= bounds.Position.X && minX <= bounds.End.X &&
                maxY >= bounds.Position.Y && minY <= bounds.End.Y)
            {
                result.Add(seg);
            }
        }
        return result;
    }

    /// <summary>
    /// Create river texture for GPU rendering
    /// </summary>
    public static Texture2D CreateRiverTexture()
    {
        if (_riverData == null) return null;

        var image = Image.CreateEmpty(MapWidth, MapHeight, false, Image.Format.R8);
        image.SetData(MapWidth, MapHeight, false, Image.Format.R8, _riverData);
        return ImageTexture.CreateFromImage(image);
    }
}