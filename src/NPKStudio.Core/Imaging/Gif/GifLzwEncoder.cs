namespace NPKStudio.Core.Imaging.Gif;

/// <summary>
///     GIF 的 LZW 压缩（变长码 9..12 位，输出为 ≤255 字节的数据子块）。
///     <para>
///         实现移植自经典的 GIF LZW（与 <c>compress</c> 同源的哈希表版本），要点：
///         <list type="number">
///             <item>码宽从 <c>minCodeSize + 1</c> 起；</item>
///             <item>码值上限（<c>(1 &lt;&lt; 码宽) - 1</c>）写满后码宽 +1，最大 12 位；</item>
///             <item>字典写满 4096 时发 Clear Code 并重置；</item>
///             <item>位流按 LSB-first 打包，每个子块最多 255 字节。</item>
///         </list>
///     </para>
///     <para>
///         这里不用 <see cref="Dictionary{TKey,TValue}" /> 做字典：每个输入符号都要查找，
///         固定 5003 槽的线性探测哈希表没有分配、也没有装箱。
///     </para>
/// </summary>
internal sealed class GifLzwEncoder
{
    private const int HashSize = 5003;
    private const int MaxBits = 12;
    private const int MaxMaxCode = 1 << MaxBits;

    private readonly Stream _output;
    private readonly int _minCodeSize;
    private readonly int _clearCode;
    private readonly int _endCode;
    private readonly int[] _hashTable = new int[HashSize];
    private readonly int[] _codeTable = new int[HashSize];
    private readonly byte[] _block = new byte[255];
    private readonly int _hashShift;

    private int _blockLength;
    private int _accumulator;
    private int _accumulatorBits;
    private int _codeSize;
    private int _maxCode;
    private int _nextCode;
    private bool _clearPending;

    /// <summary>
    ///     创建编码器。
    /// </summary>
    /// <param name="output">目标流。</param>
    /// <param name="minCodeSize">LZW 最小码宽（GIF 规定 ≥2）。</param>
    internal GifLzwEncoder(Stream output, int minCodeSize)
    {
        _output = output;
        _minCodeSize = minCodeSize;
        _clearCode = 1 << minCodeSize;
        _endCode = _clearCode + 1;

        // 哈希槽位与位移：hsize 5003 → 4 位位移（同经典实现）。
        var size = HashSize;
        var shift = 0;
        while (size < 65536)
        {
            size *= 2;
            shift++;
        }

        _hashShift = 8 - shift;
    }

    /// <summary>
    ///     压缩一段色号数据并写出完整数据子块（含结尾 0x00）。
    /// </summary>
    /// <param name="indices">每像素 1 字节的色号。</param>
    internal void Encode(ReadOnlySpan<byte> indices)
    {
        if (indices.Length == 0)
        {
            // 理论上 RgbaImage 不会出现 0 像素，但 GIF 仍要求一对 Clear/EOI。
            ResetCodes();
            Array.Fill(_hashTable, -1);
            Output(_clearCode);
            Output(_endCode);
            FlushAccumulator();
            FlushBlock();
            _output.WriteByte(0);
            return;
        }

        ResetCodes();
        Array.Fill(_hashTable, -1);
        Output(_clearCode);

        var ent = (int)indices[0];
        for (var i = 1; i < indices.Length; i++)
        {
            var c = indices[i];
            var code = (c << MaxBits) + ent;
            var hashIndex = (c << _hashShift) ^ ent;

            if (_hashTable[hashIndex] == code)
            {
                ent = _codeTable[hashIndex];
                continue;
            }

            if (_hashTable[hashIndex] >= 0)
            {
                // 线性探测：步长 disp 与表长互质（5003 是质数）。
                var disp = HashSize - hashIndex;
                if (hashIndex == 0)
                {
                    disp = 1;
                }

                var found = false;
                do
                {
                    hashIndex -= disp;
                    if (hashIndex < 0)
                    {
                        hashIndex += HashSize;
                    }

                    if (_hashTable[hashIndex] == code)
                    {
                        ent = _codeTable[hashIndex];
                        found = true;
                        break;
                    }
                } while (_hashTable[hashIndex] >= 0);

                if (found)
                {
                    continue;
                }
            }

            Output(ent);
            ent = c;

            if (_nextCode < MaxMaxCode)
            {
                _codeTable[hashIndex] = _nextCode++;
                _hashTable[hashIndex] = code;
            }
            else
            {
                // 字典满：发 Clear Code，码宽回到初始值，从头建表。
                Array.Fill(_hashTable, -1);
                _nextCode = _clearCode + 2;
                _clearPending = true;
                Output(_clearCode);
            }
        }

        Output(ent);
        Output(_endCode);
        FlushAccumulator();
        FlushBlock();
        _output.WriteByte(0);
    }

    private void ResetCodes()
    {
        _codeSize = _minCodeSize + 1;
        _maxCode = (1 << _codeSize) - 1;
        _nextCode = _clearCode + 2;
        _clearPending = false;
        _blockLength = 0;
        _accumulator = 0;
        _accumulatorBits = 0;
    }

    private void Output(int code)
    {
        // 低位先出：先写已有位，再把新码左移到高位。
        _accumulator &= (1 << _accumulatorBits) - 1;
        if (_accumulatorBits > 0)
        {
            _accumulator |= code << _accumulatorBits;
        }
        else
        {
            _accumulator = code;
        }

        _accumulatorBits += _codeSize;

        while (_accumulatorBits >= 8)
        {
            AppendByte((byte)(_accumulator & 0xFF));
            _accumulator >>= 8;
            _accumulatorBits -= 8;
        }

        // 码宽切换必须发生在"写出当前码之后、写出下一个码之前"。
        if (_nextCode > _maxCode || _clearPending)
        {
            if (_clearPending)
            {
                _codeSize = _minCodeSize + 1;
                _maxCode = (1 << _codeSize) - 1;
                _clearPending = false;
            }
            else
            {
                _codeSize++;
                _maxCode = _codeSize == MaxBits ? MaxMaxCode : (1 << _codeSize) - 1;
            }
        }
    }

    private void AppendByte(byte value)
    {
        _block[_blockLength++] = value;
        if (_blockLength == 255)
        {
            FlushBlock();
        }
    }

    private void FlushBlock()
    {
        if (_blockLength == 0)
        {
            return;
        }

        _output.WriteByte((byte)_blockLength);
        _output.Write(_block, 0, _blockLength);
        _blockLength = 0;
    }

    private void FlushAccumulator()
    {
        while (_accumulatorBits > 0)
        {
            AppendByte((byte)(_accumulator & 0xFF));
            _accumulator >>= 8;
            _accumulatorBits -= 8;
        }
    }
}
