using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

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
    private Dictionary<string, int>? _byteEncoder;
    private bool _isLoaded;
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    private const int ImageSize = 384;
    private const float PixelMean = 0.5f;
    private const float PixelStd = 0.5f;
    private const int BosTokenId = 0;
    private const int EosTokenId = 2;
    private const int MaxNewTokens = 128;

    public bool IsModelLoaded => _isLoaded;

    public async Task LoadModelsAsync(string modelFolder)
    {
        await _loadLock.WaitAsync();
        try
        {
            if (_isLoaded) return;

            var encoderPath = Path.Combine(modelFolder, "encoder_model.onnx");
            var decoderPath = Path.Combine(modelFolder, "decoder_model.onnx");
            var vocabPath = Path.Combine(modelFolder, "vocab.json");

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
                _encoder = new InferenceSession(encoderPath, options);
                _decoder = new InferenceSession(decoderPath, options);
            });

            var vocabJson = await File.ReadAllTextAsync(vocabPath);
            var tokenToId = JsonSerializer.Deserialize<Dictionary<string, int>>(vocabJson)
                            ?? throw new InvalidDataException("Failed to parse vocab.json");

            _idToToken = tokenToId.ToDictionary(kv => kv.Value, kv => kv.Key);
            _byteEncoder = BuildByteEncoder();
            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<string> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (!_isLoaded)
            throw new InvalidOperationException("Call LoadModelsAsync before recognizing.");

        return await Task.Run(() => RunInference(imagePath), cancellationToken);
    }

    private string RunInference(string imagePath)
    {
        var pixelValues = PreprocessImage(imagePath);

        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values",
                new DenseTensor<float>(pixelValues, [1, 3, ImageSize, ImageSize]))
        };

        float[] hiddenStateData;
        int[] hiddenStateDims;

        using (var encoderOutput = _encoder!.Run(encoderInputs))
        {
            var hiddenState = encoderOutput.First().AsTensor<float>();
            hiddenStateData = hiddenState.ToArray();
            hiddenStateDims = hiddenState.Dimensions.ToArray();
        }

        var tokenIds = GenerateTokens(hiddenStateData, hiddenStateDims);
        return DecodeTokens(tokenIds);
    }

    private List<int> GenerateTokens(float[] hiddenStateData, int[] hiddenStateDims)
    {
        var inputIds = new List<int> { BosTokenId };
        var encSeqLen = hiddenStateDims[1];
        var decoderInputNames = new HashSet<string>(_decoder!.InputMetadata.Keys);

        for (int step = 0; step < MaxNewTokens; step++)
        {
            var inputIdsLong = inputIds.Select(x => (long)x).ToArray();

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids",
                    new DenseTensor<long>(inputIdsLong, [1, inputIds.Count])),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states",
                    new DenseTensor<float>(hiddenStateData, hiddenStateDims))
            };

            if (decoderInputNames.Contains("encoder_attention_mask"))
            {
                var mask = Enumerable.Repeat(1L, encSeqLen).ToArray();
                inputs.Add(NamedOnnxValue.CreateFromTensor("encoder_attention_mask",
                    new DenseTensor<long>(mask, [1, encSeqLen])));
            }

            int nextToken;
            using (var output = _decoder.Run(inputs))
            {
                var logits = output.First().AsTensor<float>();
                var vocabSize = logits.Dimensions[2];
                var lastPos = inputIds.Count - 1;

                nextToken = 0;
                float maxLogit = float.NegativeInfinity;
                for (int v = 0; v < vocabSize; v++)
                {
                    var l = logits[0, lastPos, v];
                    if (l > maxLogit) { maxLogit = l; nextToken = v; }
                }
            }

            if (nextToken == EosTokenId) break;
            inputIds.Add(nextToken);
        }

        return inputIds.Count > 1 ? inputIds.GetRange(1, inputIds.Count - 1) : [];
    }

    private string DecodeTokens(List<int> tokenIds)
    {
        if (_idToToken is null || _byteEncoder is null) return string.Empty;

        var sb = new StringBuilder();
        foreach (var id in tokenIds)
        {
            if (!_idToToken.TryGetValue(id, out var token)) continue;

            var bytes = new List<byte>(token.Length);
            foreach (var ch in token)
            {
                if (_byteEncoder.TryGetValue(ch.ToString(), out var b))
                    bytes.Add((byte)b);
            }
            if (bytes.Count > 0)
                sb.Append(Encoding.UTF8.GetString(bytes.ToArray()));
        }

        return sb.ToString().Trim();
    }

    private static float[] PreprocessImage(string imagePath)
    {
        using var original = new Bitmap(imagePath);
        using var resized = new Bitmap(ImageSize, ImageSize, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(resized))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(original, 0, 0, ImageSize, ImageSize);
        }

        var bmpData = resized.LockBits(
            new Rectangle(0, 0, ImageSize, ImageSize),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        var rawBytes = new byte[bmpData.Stride * ImageSize];
        Marshal.Copy(bmpData.Scan0, rawBytes, 0, rawBytes.Length);
        resized.UnlockBits(bmpData);

        var pixels = new float[3 * ImageSize * ImageSize];
        int stride = bmpData.Stride;

        for (int y = 0; y < ImageSize; y++)
        {
            for (int x = 0; x < ImageSize; x++)
            {
                int bmpIdx = y * stride + x * 3;
                // Bitmap stores BGR; TrOCR expects RGB
                float b = rawBytes[bmpIdx] / 255f;
                float gv = rawBytes[bmpIdx + 1] / 255f;
                float r = rawBytes[bmpIdx + 2] / 255f;

                int pixIdx = y * ImageSize + x;
                pixels[0 * ImageSize * ImageSize + pixIdx] = (r - PixelMean) / PixelStd;
                pixels[1 * ImageSize * ImageSize + pixIdx] = (gv - PixelMean) / PixelStd;
                pixels[2 * ImageSize * ImageSize + pixIdx] = (b - PixelMean) / PixelStd;
            }
        }

        return pixels;
    }

    // GPT-2 byte-level BPE: maps each unicode character in a token back to its original byte value.
    private static Dictionary<string, int> BuildByteEncoder()
    {
        var bs = new List<int>();
        for (int i = '!'; i <= '~'; i++) bs.Add(i);
        for (int i = '¡'; i <= '¬'; i++) bs.Add(i);
        for (int i = '®'; i <= 'ÿ'; i++) bs.Add(i);

        var cs = new List<int>(bs);
        int n = 0;
        for (int b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n++);
            }
        }

        var result = new Dictionary<string, int>(bs.Count);
        for (int i = 0; i < bs.Count; i++)
            result[char.ConvertFromUtf32(cs[i])] = bs[i];

        return result;
    }

    public void Dispose()
    {
        _encoder?.Dispose();
        _decoder?.Dispose();
        _loadLock.Dispose();
    }
}
