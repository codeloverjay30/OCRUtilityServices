using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OCRUtilityServices.Paddle;

/// <summary>
/// Runs a restricted PaddleOCR DB detector and CTC recognizer with ONNX Runtime.
/// Model input and output shapes are checked at runtime and unsupported variants fail closed.
/// </summary>
public sealed class PaddleOcrOnnxBackend : IDisposable
{
    private readonly InferenceSession _detector;
    private readonly InferenceSession _recognizer;
    private readonly string[] _dictionary;
    private readonly PaddleOcrOptions _options;
    private readonly object _runLock = new();
    private bool _disposed;

    /// <summary>Loads model sessions and the CTC dictionary.</summary>
    public PaddleOcrOnnxBackend(PaddleOcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        if (options.DetectionSize <= 0 || options.RecognitionHeight <= 0 ||
            options.RecognitionWidth <= 0 || options.MinimumComponentArea <= 0 ||
            options.DetectionThreshold is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid PaddleOCR model configuration.");
        _dictionary = File.ReadAllLines(options.DictionaryPath);
        if (_dictionary.Length == 0)
            throw new InvalidDataException("The PaddleOCR CTC dictionary is empty.");
        _detector = new InferenceSession(options.DetectionModelPath);
        try { _recognizer = new InferenceSession(options.RecognitionModelPath); }
        catch { _detector.Dispose(); throw; }
    }

    /// <summary>Recognizes lines and local bounds in an already-decoded image.</summary>
    public IReadOnlyList<PaddleOcrDetectedLine> Recognize(Image<Rgb24> source, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_runLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var resized = source.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(_options.DetectionSize, _options.DetectionSize),
                Mode = ResizeMode.Stretch
            }));
            var detectionInput = MakeTensor(resized, normalize: true);
            var detection = Run(_detector, detectionInput);
            if (detection.Dimensions.Length != 4 || detection.Dimensions[0] != 1 ||
                detection.Dimensions[1] != 1)
                throw new NotSupportedException("Expected DB detection output [1,1,H,W].");
            int h = detection.Dimensions[2], w = detection.Dimensions[3];
            if (h <= 0 || w <= 0) throw new InvalidDataException("Invalid DB output dimensions.");
            var boxes = FindComponents(detection, w, h, cancellationToken);
            var lines = new List<PaddleOcrDetectedLine>();
            foreach (var box in boxes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int left = Math.Clamp((int)Math.Floor(box.X * (double)source.Width / w), 0, source.Width - 1);
                int top = Math.Clamp((int)Math.Floor(box.Y * (double)source.Height / h), 0, source.Height - 1);
                int right = Math.Clamp((int)Math.Ceiling((box.X + box.Width) * (double)source.Width / w), left + 1, source.Width);
                int bottom = Math.Clamp((int)Math.Ceiling((box.Y + box.Height) * (double)source.Height / h), top + 1, source.Height);
                using var crop = source.Clone(ctx => ctx.Crop(new SixLabors.ImageSharp.Rectangle(left, top, right - left, bottom - top))
                    .Resize(new ResizeOptions { Size = new Size(_options.RecognitionWidth, _options.RecognitionHeight), Mode = ResizeMode.Stretch }));
                var recognized = Run(_recognizer, MakeTensor(crop, normalize: false));
                string text = DecodeCtc(recognized);
                if (!string.IsNullOrWhiteSpace(text))
                    lines.Add(new PaddleOcrDetectedLine(text, left, top, right - left, bottom - top));
            }
            return lines;
        }
    }


    /// <summary>
    /// Compares two recognition resize strategies for the same source-image crop.
    /// The detector and the production recognition path remain unchanged.
    /// </summary>
    /// <param name="source">The original screenshot.</param>
    /// <param name="left">The source-image crop's left coordinate.</param>
    /// <param name="top">The source-image crop's top coordinate.</param>
    /// <param name="width">The source-image crop width.</param>
    /// <param name="height">The source-image crop height.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Both recognition results and the aspect-preserving content width.</returns>
    public PaddleOcrRecognitionComparison CompareRecognitionPreprocessing(
        Image<Rgb24> source,
        int left,
        int top,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (left < 0 || top < 0 || width <= 0 || height <= 0 ||
            (long)left + width > source.Width ||
            (long)top + height > source.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The recognition crop must be within the source image.");
        }

        lock (_runLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            // A 組：RGB + Stretch
            using var crop = source.Clone(ctx => ctx.Crop(
                new SixLabors.ImageSharp.Rectangle(left, top, width, height)));

            using var stretched = crop.Clone(ctx => ctx.Resize(
                new ResizeOptions
                {
                    Size = new Size(_options.RecognitionWidth, _options.RecognitionHeight),
                    Mode = ResizeMode.Stretch
                }));

            string stretchText = DecodeCtc(
                Run(_recognizer, MakeTensor(stretched, normalize: false)));

            cancellationToken.ThrowIfCancellationRequested();

            // B 組：RGB + AspectRatio Padding
            int scaledWidth = Math.Clamp(
                (int)Math.Round(
                    width * (double)_options.RecognitionHeight / height,
                    MidpointRounding.AwayFromZero),
                1,
                _options.RecognitionWidth);

            using var scaled = crop.Clone(ctx => ctx.Resize(
                new ResizeOptions
                {
                    Size = new Size(scaledWidth, _options.RecognitionHeight),
                    Mode = ResizeMode.Stretch
                }));

            using var padded = new Image<Rgb24>(
                _options.RecognitionWidth,
                _options.RecognitionHeight,
                new Rgb24(255, 255, 255));

            padded.Mutate(ctx => ctx.DrawImage(scaled, new Point(0, 0), 1f));

            string paddedText = DecodeCtc(
                Run(_recognizer, MakeTensor(padded, normalize: false)));

            // C: BGR + Stretch
            cancellationToken.ThrowIfCancellationRequested();

            string bgrStretchText = DecodeCtc(
                Run(
                    _recognizer,
                    MakeRecognitionTensor(stretched, useBgr: true)));

            // D: BGR + AspectRatio Padding
            cancellationToken.ThrowIfCancellationRequested();

            string bgrPaddedText = DecodeCtc(
                Run(
                    _recognizer,
                    MakeRecognitionTensor(padded, useBgr: true)));

            return new PaddleOcrRecognitionComparison(
                stretchText,
                paddedText,
                bgrStretchText,
                bgrPaddedText,
                scaledWidth,
                _options.RecognitionWidth,
                _options.RecognitionHeight);
        }
    }

    /// <summary>
    /// Recognizes a specified source-image crop using RGB normalization and stretch resizing.
    /// This diagnostic path does not perform text detection or modify production recognition.
    /// </summary>
    /// <param name="source">The original screenshot.</param>
    /// <param name="left">The left coordinate of the crop.</param>
    /// <param name="top">The top coordinate of the crop.</param>
    /// <param name="width">The crop width.</param>
    /// <param name="height">The crop height.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The CTC-decoded recognition text.</returns>
    public string RecognizeDiagnosticCropRgbStretch(
        Image<Rgb24> source,
        int left,
        int top,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        if (left < 0 || top < 0 || width <= 0 || height <= 0 ||
            (long)left + width > source.Width ||
            (long)top + height > source.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The recognition crop must be within the source image.");
        }

        lock (_runLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            using var crop = source.Clone(ctx => ctx.Crop(
                new SixLabors.ImageSharp.Rectangle(left, top, width, height))
                .Resize(new ResizeOptions
                {
                    Size = new Size(_options.RecognitionWidth, _options.RecognitionHeight),
                    Mode = ResizeMode.Stretch
                }));

            cancellationToken.ThrowIfCancellationRequested();

            return DecodeCtc(
                Run(_recognizer, MakeTensor(crop, normalize: false)));
        }
    }

    /// <summary>
    /// Inspects recognition CTC scores and dictionary entries for a specified crop.
    /// This method does not change the production recognition path.
    /// </summary>
    /// <param name="source">The original screenshot.</param>
    /// <param name="left">The crop left coordinate.</param>
    /// <param name="top">The crop top coordinate.</param>
    /// <param name="width">The crop width.</param>
    /// <param name="height">The crop height.</param>
    /// <param name="expectedCharacters">Characters to inspect in the dictionary.</param>
    /// <param name="topK">The number of candidates to report per time step.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A snapshot of CTC candidates and dictionary coverage.</returns>
    public PaddleOcrCtcDiagnosticResult DiagnoseCtcTopK(
        Image<Rgb24> source,
        int left,
        int top,
        int width,
        int height,
        string expectedCharacters,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCharacters);
        if (topK <= 0)
            throw new ArgumentOutOfRangeException(nameof(topK), "Top-K must be positive.");
        if (left < 0 || top < 0 || width <= 0 || height <= 0 ||
            (long)left + width > source.Width ||
            (long)top + height > source.Height)
            throw new ArgumentOutOfRangeException(
                nameof(width), "The recognition crop must be within the source image.");

        cancellationToken.ThrowIfCancellationRequested();
        lock (_runLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellationToken.ThrowIfCancellationRequested();

            using var crop = source.Clone(ctx => ctx.Crop(
                new SixLabors.ImageSharp.Rectangle(left, top, width, height))
                .Resize(new ResizeOptions
                {
                    Size = new Size(_options.RecognitionWidth, _options.RecognitionHeight),
                    Mode = ResizeMode.Stretch
                }));

            Tensor<float> logits = Run(_recognizer, MakeTensor(crop, normalize: false));
            if (logits.Dimensions.Length != 3 || logits.Dimensions[0] != 1 ||
                logits.Dimensions[1] <= 0 ||
                logits.Dimensions[2] != _dictionary.Length + 2)
                throw new NotSupportedException(
                    "Expected PaddleOCR CTC output [1,T,dictionaryLength+2].");

            int timeSteps = logits.Dimensions[1];
            int classCount = logits.Dimensions[2];
            int spaceIndex = _dictionary.Length + 1;
            int candidateCount = Math.Min(topK, classCount);
            var frames = new List<PaddleOcrCtcTimeStep>(timeSteps);

            for (int t = 0; t < timeSteps; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var best = new List<PaddleOcrCtcCandidate>(candidateCount);
                for (int c = 0; c < classCount; c++)
                {
                    float score = logits[0, t, c];
                    if (!float.IsFinite(score))
                        throw new InvalidDataException(
                            $"Non-finite CTC score at time step {t}, class {c}.");

                    if (best.Count == candidateCount &&
                        score <= best[^1].Score)
                        continue;

                    string label = c == 0 ? "<BLANK>" :
                        c == spaceIndex ? "<SPACE>" : _dictionary[c - 1];
                    var candidate = new PaddleOcrCtcCandidate(c, label, score);
                    int insertAt = best.FindIndex(item =>
                        score > item.Score ||
                        (score == item.Score && c < item.ClassIndex));
                    if (insertAt < 0)
                        best.Add(candidate);
                    else
                        best.Insert(insertAt, candidate);
                    if (best.Count > candidateCount)
                        best.RemoveAt(best.Count - 1);
                }

                frames.Add(new PaddleOcrCtcTimeStep(t, best.ToArray()));
            }

            var coverage = new List<PaddleOcrCtcDictionaryEntry>();
            foreach (char character in expectedCharacters.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var indices = new List<int>();
                for (int i = 0; i < _dictionary.Length; i++)
                {
                    if (_dictionary[i] == character.ToString())
                        indices.Add(i + 1);
                }
                coverage.Add(new PaddleOcrCtcDictionaryEntry(
                    character.ToString(), indices.ToArray()));
            }

            return new PaddleOcrCtcDiagnosticResult(
                DecodeCtc(logits),
                classCount,
                timeSteps,
                coverage.ToArray(),
                frames.ToArray());
        }
    }

    /// <summary>Disposes both inference sessions.</summary>
    public void Dispose()
    {
        lock (_runLock)
        {
            if (_disposed) return;
            _disposed = true;
            _recognizer.Dispose();
            _detector.Dispose();
        }
    }

    /// <summary>Runs an ONNX model with a single float tensor input.</summary>
    private static Tensor<float> Run(InferenceSession session, DenseTensor<float> input)
    {
        string inputName = session.InputMetadata.Keys.Single();
        using var results = session.Run(new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });
        var output = results.Single().AsTensor<float>();
        return new DenseTensor<float>(output.ToArray(), output.Dimensions.ToArray());
    }

    /// <summary>Converts RGB pixels into a CHW float tensor.</summary>
    private static DenseTensor<float> MakeTensor(Image<Rgb24> image, bool normalize)
    {
        var tensor = new DenseTensor<float>(new[] { 1, 3, image.Height, image.Width });
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                Span<Rgb24> row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    var p = row[x];
                    float r = p.R / 255f, g = p.G / 255f, b = p.B / 255f;
                    tensor[0, 0, y, x] = normalize ? (r - 0.485f) / 0.229f : (r - 0.5f) / 0.5f;
                    tensor[0, 1, y, x] = normalize ? (g - 0.456f) / 0.224f : (g - 0.5f) / 0.5f;
                    tensor[0, 2, y, x] = normalize ? (b - 0.406f) / 0.225f : (b - 0.5f) / 0.5f;
                }
            }
        });
        return tensor;
    }

    /// <summary>
    /// Converts recognition image pixels into a normalized CHW tensor
    /// using either RGB or BGR channel order.
    /// </summary>
    /// <param name="image">The recognition input image.</param>
    /// <param name="useBgr">Whether to use BGR instead of RGB.</param>
    /// <returns>The normalized recognition input tensor.</returns>
    private static DenseTensor<float> MakeRecognitionTensor(
        Image<Rgb24> image,
        bool useBgr)
    {
        ArgumentNullException.ThrowIfNull(image);

        var tensor = new DenseTensor<float>(
            new[] { 1, 3, image.Height, image.Width });

        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < image.Height; y++)
            {
                ReadOnlySpan<Rgb24> row = accessor.GetRowSpan(y);

                for (int x = 0; x < row.Length; x++)
                {
                    Rgb24 pixel = row[x];

                    byte channel0 = useBgr ? pixel.B : pixel.R;
                    byte channel1 = pixel.G;
                    byte channel2 = useBgr ? pixel.R : pixel.B;

                    tensor[0, 0, y, x] =
                        (channel0 / 255f - 0.5f) / 0.5f;

                    tensor[0, 1, y, x] =
                        (channel1 / 255f - 0.5f) / 0.5f;

                    tensor[0, 2, y, x] =
                        (channel2 / 255f - 0.5f) / 0.5f;
                }
            }
        });

        return tensor;
    }

    /// <summary>Extracts axis-aligned candidate components from a DB probability map.</summary>
    private List<(int X, int Y, int Width, int Height)> FindComponents(Tensor<float> map, int width, int height, CancellationToken token)
    {
        var visited = new bool[checked(width * height)];
        var boxes = new List<(int, int, int, int)>();
        var queue = new Queue<int>();
        for (int start = 0; start < visited.Length; start++)
        {
            if ((start & 4095) == 0) token.ThrowIfCancellationRequested();
            int sx = start % width, sy = start / width;
            if (visited[start] || map[0, 0, sy, sx] < _options.DetectionThreshold) continue;
            visited[start] = true;
            queue.Enqueue(start);
            int minX = sx, maxX = sx, minY = sy, maxY = sy, count = 0;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue(), x = current % width, y = current / width;
                count++;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                Visit(x - 1, y); Visit(x + 1, y); Visit(x, y - 1); Visit(x, y + 1);
            }
            if (count >= _options.MinimumComponentArea)
                boxes.Add((minX, minY, maxX - minX + 1, maxY - minY + 1));

            void Visit(int x, int y)
            {
                if ((uint)x >= (uint)width || (uint)y >= (uint)height) return;
                int index = y * width + x;
                if (visited[index] || map[0, 0, y, x] < _options.DetectionThreshold) return;
                visited[index] = true;
                queue.Enqueue(index);
            }
        }
        return boxes.OrderBy(b => b.Item2).ThenBy(b => b.Item1).ToList();
    }

    /// <summary>
    /// Decodes PaddleOCR CTC output using a blank token followed by
    /// dictionary characters and an optional trailing space token.
    /// </summary>
    /// <param name="logits">The recognition output tensor in [1, T, C] format.</param>
    /// <returns>The decoded text after CTC duplicate and blank removal.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the recognition output has an unsupported shape.
    /// </exception>
    private string DecodeCtc(Tensor<float> logits)
    {
        ArgumentNullException.ThrowIfNull(logits);

        int dictionaryLength = _dictionary.Length;

        if (logits.Dimensions.Length != 3 ||
            logits.Dimensions[0] != 1 ||
            logits.Dimensions[1] <= 0 ||
            logits.Dimensions[2] != dictionaryLength + 2)
        {
            throw new NotSupportedException(
                "Expected PaddleOCR CTC output [1,T,dictionaryLength+2].");
        }

        int timeSteps = logits.Dimensions[1];
        int classCount = logits.Dimensions[2];
        int spaceIndex = dictionaryLength + 1;

        var builder = new System.Text.StringBuilder();
        int previous = -1;

        for (int t = 0; t < timeSteps; t++)
        {
            int best = 0;
            float bestScore = float.NegativeInfinity;

            for (int c = 0; c < classCount; c++)
            {
                float score = logits[0, t, c];

                if (!float.IsFinite(score))
                {
                    throw new InvalidDataException(
                        $"Non-finite CTC score at time step {t}, class {c}.");
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            if (best != 0 && best != previous)
            {
                if (best == spaceIndex)
                {
                    builder.Append(' ');
                }
                else
                {
                    builder.Append(_dictionary[best - 1]);
                }
            }

            previous = best;
        }

        return builder.ToString();
    }
}

/// <summary>Represents one detected OCR text line in local image coordinates.</summary>
public sealed record PaddleOcrDetectedLine(string Text, int X, int Y, int Width, int Height);

/// <summary>
/// Holds recognition results for RGB/BGR and resize strategy comparisons.
/// </summary>
public sealed record PaddleOcrRecognitionComparison(
    string StretchText,
    string AspectRatioPaddedText,
    string BgrStretchText,
    string BgrAspectRatioPaddedText,
    int ContentWidth,
    int InputWidth,
    int InputHeight);

/// <summary>
/// Represents a CTC candidate with its raw model score.
/// </summary>
public sealed record PaddleOcrCtcCandidate(int ClassIndex, string Label, float Score);

/// <summary>
/// Represents the highest-scoring CTC candidates at one time step.
/// </summary>
public sealed record PaddleOcrCtcTimeStep(
    int TimeStep,
    IReadOnlyList<PaddleOcrCtcCandidate> Candidates);

/// <summary>
/// Represents all class indices matching a dictionary character.
/// </summary>
public sealed record PaddleOcrCtcDictionaryEntry(
    string Character,
    IReadOnlyList<int> ClassIndices);

/// <summary>
/// Represents a complete CTC recognition diagnostic snapshot.
/// </summary>
public sealed record PaddleOcrCtcDiagnosticResult(
    string DecodedText,
    int ClassCount,
    int TimeStepCount,
    IReadOnlyList<PaddleOcrCtcDictionaryEntry> DictionaryCoverage,
    IReadOnlyList<PaddleOcrCtcTimeStep> TimeSteps);

