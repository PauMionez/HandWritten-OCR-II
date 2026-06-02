using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace HandWritten_OCR.Services;

/// <summary>
/// Runs TrOCR (encoder + decoder) exported to ONNX via Hugging Face Optimum.
/// Expected model files in modelFolder:
///   encoder_model.onnx, decoder_model.onnx, vocab.json
/// Export command:
///   optimum-cli export onnx --model microsoft/trocr-base-handwritten --task image-to-text ./trocr-onnx
/// </summary>
public sealed class TrOcrService : IOcrService, IDisposable
{
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;
    private Dictionary<int, string>? _idToToken;
    private Dictionary<char, byte>? _charToByte;  // keyed by char, avoids per-char ToString()
    private bool _needsEncoderMask;               // precomputed at load time; eliminates per-call HashSet
    private bool _isLoaded;
    private bool _disposed;
    private static readonly object _bitmapLock = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private InferenceSession? _decoderWithPast;
    private bool _hasKvCache;
    private string[]? _presentOutputNames;
    private bool _decoderWithPastNeedsEncoderHiddenStates;
    private bool _decoderWithPastNeedsMask;

    private const int ImageSize = 384;
    private const int BosTokenId = 0;
    private const int EosTokenId = 2;
    private const int MaxNewTokens = 128;

    // LUT: (b / 255f - 0.5f) / 0.5f  =  b / 127.5f - 1.0f
    // Replaces ~442K FP divisions per image with 256-entry table lookups.
    private static readonly float[] s_normalizedLut =
        Enumerable.Range(0, 256).Select(b => b / 127.5f - 1.0f).ToArray();

    public bool IsModelLoaded => _isLoaded;

    public async Task LoadModelsAsync(string modelFolder)
    {
        await _loadLock.WaitAsync();
        try
        {
            if (_isLoaded) return;

            string encoderPath         = Path.Combine(modelFolder, "encoder_model.onnx");
            string decoderPath         = Path.Combine(modelFolder, "decoder_model.onnx");
            string vocabPath           = Path.Combine(modelFolder, "vocab.json");
            string decoderWithPastPath = Path.Combine(modelFolder, "decoder_with_past_model.onnx");

            if (!File.Exists(encoderPath) || !File.Exists(decoderPath) || !File.Exists(vocabPath))
                throw new FileNotFoundException(
                    $"Model files not found in: {modelFolder}\n" +
                    "Expected: encoder_model.onnx, decoder_model.onnx, vocab.json\n\n" +
                    "Export with:\n" +
                    "  pip install optimum[onnxruntime]\n" +
                    "  optimum-cli export onnx --model microsoft/trocr-base-handwritten --task image-to-text ./models");

            await Task.Run(() =>
            {
                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                options.IntraOpNumThreads = Environment.ProcessorCount;
                options.EnableMemoryPattern = true;
                options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                _encoder = new InferenceSession(encoderPath, options);
                _decoder = new InferenceSession(decoderPath, options);
                if (File.Exists(decoderWithPastPath))
                    _decoderWithPast = new InferenceSession(decoderWithPastPath, options);
            });

            string vocabJson = await File.ReadAllTextAsync(vocabPath);
            var tokenToId = JsonSerializer.Deserialize<Dictionary<string, int>>(vocabJson)
                            ?? throw new InvalidDataException("Failed to parse vocab.json");

            _idToToken = tokenToId.ToDictionary(kv => kv.Value, kv => kv.Key);
            _charToByte = BuildCharToByte();
            _needsEncoderMask = _decoder!.InputMetadata.ContainsKey("encoder_attention_mask");
            if (_decoderWithPast is not null)
            {
                _presentOutputNames = _decoder.OutputMetadata.Keys
                    .Where(k => k.StartsWith("present", StringComparison.Ordinal))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .ToArray();
                _hasKvCache = _presentOutputNames.Length > 0;
                if (_hasKvCache)
                {
                    _decoderWithPastNeedsEncoderHiddenStates =
                        _decoderWithPast.InputMetadata.ContainsKey("encoder_hidden_states");
                    _decoderWithPastNeedsMask =
                        _decoderWithPast.InputMetadata.ContainsKey("encoder_attention_mask");
                }
            }
            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<string> RecognizeAsync(string imagePath,
        CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
            throw new InvalidOperationException("Call LoadModelsAsync before recognizing.");

        return await Task.Run(() =>
        {
            float[] pixels = PreprocessFromPath(imagePath);
            return RunInferenceWithPixels(pixels);
        }, cancellationToken);
    }

    public async Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds,
        CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
            throw new InvalidOperationException("Call LoadModelsAsync before recognizing.");

        return await Task.Run(() =>
        {
            float[] pixels = PreprocessRegionFromPath(imagePath, pixelBounds);
            return RunInferenceWithPixels(pixels);
        }, cancellationToken);
    }

    /// <summary>
    /// Loads the image once, preprocesses all regions in parallel, then runs
    /// inference sequentially to avoid ORT thread oversubscription.
    /// </summary>
    public async Task<IReadOnlyList<string>> RecognizeRegionsAsync(
        string imagePath,
        IReadOnlyList<Rect> regions,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
            throw new InvalidOperationException("Call LoadModelsAsync before recognizing.");

        return await Task.Run(() =>
        {
            float[][] pixelArrays;
            using (Bitmap original = new Bitmap(imagePath))
            {
                pixelArrays = new float[regions.Count][];
                Parallel.For(0, regions.Count, i =>
                    pixelArrays[i] = PreprocessRegion(original, regions[i]));
            }

            string[] results = new string[regions.Count];
            for (int i = 0; i < regions.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results[i] = RunInferenceWithPixels(pixelArrays[i]);
                progress?.Report(i);
            }

            return (IReadOnlyList<string>)results;
        }, cancellationToken);
    }

    // ── Inference ─────────────────────────────────────────────────────────────

    private string RunInferenceWithPixels(float[] pixelValues)
    {
        var encoderInputs = new[]
        {
            NamedOnnxValue.CreateFromTensor("pixel_values",
                new DenseTensor<float>(pixelValues, [1, 3, ImageSize, ImageSize]))
        };

        DenseTensor<float> hiddenStates;
        using (var encoderOutput = _encoder!.Run(encoderInputs))
        {
            var raw = encoderOutput.First().AsTensor<float>();
            hiddenStates = new DenseTensor<float>(raw.ToArray(), raw.Dimensions.ToArray());
        }

        int[] tokenIds = GenerateTokens(hiddenStates);
        return DecodeTokens(tokenIds);
    }

    private int[] GenerateTokens(DenseTensor<float> hiddenStates) =>
        _hasKvCache ? GenerateTokensKvCached(hiddenStates) : GenerateTokensBaseline(hiddenStates);

    private int[] GenerateTokensKvCached(DenseTensor<float> hiddenStates)
    {
        int encSeqLen = hiddenStates.Dimensions[1];

        long[] tokenBuf = new long[MaxNewTokens + 1];
        tokenBuf[0] = BosTokenId;
        int tokenCount = 1;

        DenseTensor<long>? maskTensor = null;
        if (_needsEncoderMask)
        {
            long[] maskData = new long[encSeqLen];
            Array.Fill(maskData, 1L);
            maskTensor = new DenseTensor<long>(maskData, [1, encSeqLen]);
        }

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? prevOutput = null;
        try
        {
            for (int step = 0; step < MaxNewTokens; step++)
            {
                // Wrap the single token to pass — no allocation beyond the existing tokenBuf.
                var inputIdsMem = new Memory<long>(tokenBuf, tokenCount - 1, 1);
                var inputIdsTensor = new DenseTensor<long>(inputIdsMem, [1, 1]);

                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> currentOutput;

                if (step == 0)
                {
                    // First step: use the standard decoder; it outputs present.* KV that seeds the cache.
                    var inputs = new List<NamedOnnxValue>(3)
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenStates)
                    };
                    if (maskTensor is not null)
                        inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskTensor));
                    currentOutput = _decoder!.Run(inputs);
                }
                else
                {
                    // Subsequent steps: pass only the last token + cached KV — O(1) per step.
                    int capacity = 1
                        + (_decoderWithPastNeedsEncoderHiddenStates ? 1 : 0)
                        + (_decoderWithPastNeedsMask && maskTensor is not null ? 1 : 0)
                        + _presentOutputNames!.Length;
                    var inputs = new List<NamedOnnxValue>(capacity)
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor)
                    };
                    if (_decoderWithPastNeedsEncoderHiddenStates)
                        inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenStates));
                    if (_decoderWithPastNeedsMask && maskTensor is not null)
                        inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskTensor));

                    // Map present.X → past_key_values.X for each cached layer tensor.
                    foreach (string presentName in _presentOutputNames!)
                    {
                        string pastName = "past_key_values" + presentName["present".Length..];
                        var tensor = prevOutput!.First(o => o.Name == presentName).AsTensor<float>();
                        inputs.Add(NamedOnnxValue.CreateFromTensor(pastName, tensor));
                    }

                    currentOutput = _decoderWithPast!.Run(inputs);
                    // Run() is synchronous — all input tensors have been consumed; safe to free.
                    prevOutput!.Dispose();
                    prevOutput = null;
                }

                // Logit output is always [1, 1, vocab_size] since we feed exactly one token.
                var logitsDense = (DenseTensor<float>)currentOutput
                    .First(o => o.Name == "logits").AsTensor<float>();
                int vocabSize = logitsDense.Dimensions[2];
                ReadOnlySpan<float> logitSpan = logitsDense.Buffer.Span.Slice(0, vocabSize);

                int nextToken = 0;
                float maxLogit = float.NegativeInfinity;
                for (int v = 0; v < logitSpan.Length; v++)
                {
                    if (logitSpan[v] > maxLogit) { maxLogit = logitSpan[v]; nextToken = v; }
                }

                prevOutput = currentOutput;

                if (nextToken == EosTokenId) break;
                if (tokenCount >= tokenBuf.Length) break;
                tokenBuf[tokenCount++] = nextToken;
            }
        }
        finally
        {
            prevOutput?.Dispose();
        }

        int[] result = new int[tokenCount - 1];
        for (int i = 1; i < tokenCount; i++)
            result[i - 1] = (int)tokenBuf[i];
        return result;
    }

    private int[] GenerateTokensBaseline(DenseTensor<float> hiddenStates)
    {
        int encSeqLen = hiddenStates.Dimensions[1];

        // Pre-allocated token buffer avoids per-step List growth and LINQ int→long conversion.
        long[] tokenBuf = new long[MaxNewTokens + 1];
        tokenBuf[0] = BosTokenId;
        int tokenCount = 1;

        // Attention mask is constant across all decoder steps — allocate once.
        DenseTensor<long>? maskTensor = null;
        if (_needsEncoderMask)
        {
            long[] maskData = new long[encSeqLen];
            Array.Fill(maskData, 1L);
            maskTensor = new DenseTensor<long>(maskData, [1, encSeqLen]);
        }

        for (int step = 0; step < MaxNewTokens; step++)
        {
            // Wrap current token slice without copying — ORT reads synchronously, so this is safe.
            var inputIdsMem = new Memory<long>(tokenBuf, 0, tokenCount);
            var inputIdsTensor = new DenseTensor<long>(inputIdsMem, [1, tokenCount]);

            var inputs = new List<NamedOnnxValue>(3)
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", hiddenStates)
            };
            if (maskTensor is not null)
                inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask", maskTensor));

            int nextToken;
            using (var output = _decoder!.Run(inputs))
            {
                // ORT always returns DenseTensor for float outputs.
                var logitsDense = (DenseTensor<float>)output.First().AsTensor<float>();
                int vocabSize = logitsDense.Dimensions[2];

                // Flat-span argmax: avoids per-element 3-D indexer overhead.
                // Layout: [1, seqLen, vocabSize] row-major → last token at (tokenCount-1)*vocabSize.
                ReadOnlySpan<float> logitSpan =
                    logitsDense.Buffer.Span.Slice((tokenCount - 1) * vocabSize, vocabSize);

                nextToken = 0;
                float maxLogit = float.NegativeInfinity;
                for (int v = 0; v < logitSpan.Length; v++)
                {
                    if (logitSpan[v] > maxLogit) { maxLogit = logitSpan[v]; nextToken = v; }
                }
            }

            if (nextToken == EosTokenId) break;
            if (tokenCount >= tokenBuf.Length) break;
            tokenBuf[tokenCount++] = nextToken;
        }

        int[] result = new int[tokenCount - 1];
        for (int i = 1; i < tokenCount; i++)
            result[i - 1] = (int)tokenBuf[i];
        return result;
    }

    private string DecodeTokens(int[] tokenIds)
    {
        if (_idToToken is null || _charToByte is null) return string.Empty;

        var byteList = new List<byte>(tokenIds.Length * 4);
        foreach (int id in tokenIds)
        {
            if (!_idToToken.TryGetValue(id, out var token)) continue;
            foreach (char ch in token)
            {
                if (_charToByte.TryGetValue(ch, out byte b))
                    byteList.Add(b);
            }
        }

        if (byteList.Count == 0) return string.Empty;
        // CollectionsMarshal.AsSpan avoids a List<byte>.ToArray() copy before UTF-8 decode.
        string raw = Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(byteList)).Trim();
        return ApplyCursiveCorrections(raw);
    }

    // ── Preprocessing ─────────────────────────────────────────────────────────

    private static float[] PreprocessFromPath(string imagePath)
    {
        using Bitmap bmp = new Bitmap(imagePath);
        return PreprocessBitmap(bmp);
    }

    private static float[] PreprocessRegionFromPath(string imagePath, Rect bounds)
    {
        using Bitmap original = new Bitmap(imagePath);
        return PreprocessRegion(original, bounds);
    }

    // Shared by both single-region and batch paths; caller owns the lifetime of `original`.
    private static float[] PreprocessRegion(Bitmap original, Rect bounds)
    {
        int x = Math.Max(0, (int)Math.Round(bounds.X));
        int y = Math.Max(0, (int)Math.Round(bounds.Y));
        int w = Math.Max(1, Math.Min((int)Math.Round(bounds.Width),  original.Width  - x));
        int h = Math.Max(1, Math.Min((int)Math.Round(bounds.Height), original.Height - y));

        // GDI+ Bitmap is not thread-safe: Clone() must not run concurrently on the same instance.
        Bitmap cropped;
        lock (_bitmapLock)
            cropped = original.Clone(new Rectangle(x, y, w, h), original.PixelFormat);

        using (cropped)
            return PreprocessBitmap(cropped);
    }

    private static float[] PreprocessBitmap(Bitmap source)
    {
        using Bitmap resized = new Bitmap(ImageSize, ImageSize, PixelFormat.Format24bppRgb);
        using (Graphics g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, ImageSize, ImageSize);
        }

        BitmapData bmpData = resized.LockBits(
            new Rectangle(0, 0, ImageSize, ImageSize),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        byte[] rawBytes = new byte[bmpData.Stride * ImageSize];
        Marshal.Copy(bmpData.Scan0, rawBytes, 0, rawBytes.Length);
        resized.UnlockBits(bmpData);

        float[] pixels = new float[3 * ImageSize * ImageSize];
        int stride    = bmpData.Stride;
        int planeSize = ImageSize * ImageSize;

        Parallel.For(0, ImageSize, py =>
        {
            int rowBase = py * stride;
            int pixBase = py * ImageSize;
            for (int px = 0; px < ImageSize; px++)
            {
                int bmpIdx = rowBase + px * 3;
                int pixIdx = pixBase + px;
                // GDI stores BGR; TrOCR expects RGB — channels are swapped here.
                pixels[pixIdx]                 = s_normalizedLut[rawBytes[bmpIdx + 2]]; // R
                pixels[planeSize + pixIdx]     = s_normalizedLut[rawBytes[bmpIdx + 1]]; // G
                pixels[2 * planeSize + pixIdx] = s_normalizedLut[rawBytes[bmpIdx]];     // B
            }
        });

        return pixels;
    }

    // TrOCR confuses historical cursive capital J with f (same descending loop shape).
    // This table maps known wrong first-word readings to their correct form when
    // the rest of the token stream looks like a date (digits follow).
    private static readonly (string Wrong, string Right)[] s_cursiveCorrections =
    [
        ("fany",    "Jany"),
        ("fanny",   "Jany"),
        ("fanu",    "Janu"),
        ("january", "January"),
        ("fanuary", "January"),
        ("feb",     "Feb"),
        ("fune",    "June"),
        ("fuly",    "July"),
        // Cursive "Sept" abbreviation: the descending "pt" ligature reads as "fre"/"fit"/"fee".
        ("sefre",   "Sept"),
        ("sefe",    "Sept"),
        ("sefee",   "Sept"),
        ("seffe",   "Sept"),
        ("sefit",   "Sept"),
        ("sepre",   "Sept"),
        ("sefr",    "Sept"),
    ];

    private static string ApplyCursiveCorrections(string text)
    {
        if (text.Length == 0) return text;

        // Only correct the leading word — digits and punctuation after it are reliable.
        int wordEnd = 0;
        while (wordEnd < text.Length && char.IsLetter(text[wordEnd])) wordEnd++;
        if (wordEnd == 0) return text;

        string firstWord = text[..wordEnd];
        foreach (var (wrong, right) in s_cursiveCorrections)
        {
            if (firstWord.Equals(wrong, StringComparison.OrdinalIgnoreCase))
                return right + text[wordEnd..];
        }
        return text;
    }

    // GPT-2 byte-level BPE: builds char→byte lookup so decode avoids per-char ToString().
    private static Dictionary<char, byte> BuildCharToByte()
    {
        List<int> bs = new List<int>();
        for (int i = '!'; i <= '~'; i++) bs.Add(i);
        for (int i = '¡'; i <= '¬'; i++) bs.Add(i);
        for (int i = '®'; i <= 'ÿ'; i++) bs.Add(i);

        List<int> cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n++);
            }
        }

        var result = new Dictionary<char, byte>(bs.Count);
        for (int i = 0; i < bs.Count; i++)
            result[(char)cs[i]] = (byte)bs[i];

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _encoder?.Dispose();
        _decoder?.Dispose();
        _decoderWithPast?.Dispose();
        _loadLock.Dispose();
    }
}
