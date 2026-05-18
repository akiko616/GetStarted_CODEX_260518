using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using ReplaySystem.Core;
using UnityEngine;

namespace ReplaySystem.Compression
{
    /// <summary>압축/해제 헬퍼 클래스 (GZip, LZ4 지원)</summary>
    public static class CompressionHelper
    {
        #region Constants

        /// <summary>LZ4 매직 넘버</summary>
        private const uint LZ4_MAGIC = 0x184D2204;

        /// <summary>GZip 매직 넘버 (첫 2바이트)</summary>
        private const ushort GZIP_MAGIC = 0x8B1F;

        /// <summary>기본 버퍼 크기</summary>
        private const int DEFAULT_BUFFER_SIZE = 65536;

        #endregion

        #region Public Methods

        /// <summary>데이터를 압축합니다.</summary>
        public static byte[] Compress(byte[] data, ReCompressionType type)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }
            return type switch
            {
                ReCompressionType.None => data,
                ReCompressionType.GZip => CompressGZip(data),
                ReCompressionType.LZ4 => CompressLZ4(data),
                _ => throw new ArgumentException($"Unsupported compression type: {type}")
            };
        }

        /// <summary>데이터를 비동기로 압축합니다.</summary>
        public static async Task<byte[]> CompressAsync(byte[] data, ReCompressionType type, CancellationToken ct = default)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            return type switch
            {
                ReCompressionType.None => data,
                ReCompressionType.GZip => await CompressGZipAsync(data, ct),
                ReCompressionType.LZ4 => CompressLZ4(data), // LZ4는 이미 충분히 빠름
                _ => throw new ArgumentException($"Unsupported compression type: {type}")
            };
        }

        /// <summary>데이터를 해제합니다.</summary>
        public static byte[] Decompress(byte[] data, ReCompressionType type)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            return type switch
            {
                ReCompressionType.None => data,
                ReCompressionType.GZip => DecompressGZip(data),
                ReCompressionType.LZ4 => DecompressLZ4(data),
                _ => throw new ArgumentException($"Unsupported compression type: {type}")
            };
        }

        /// <summary>데이터를 비동기로 해제합니다.</summary>
        public static async Task<byte[]> DecompressAsync(byte[] data, ReCompressionType type, CancellationToken ct = default)
        {
            if (data == null || data.Length == 0)
            {
                return Array.Empty<byte>();
            }

            return type switch
            {
                ReCompressionType.None => data,
                ReCompressionType.GZip => await DecompressGZipAsync(data, ct),
                ReCompressionType.LZ4 => DecompressLZ4(data), // LZ4는 이미 충분히 빠름
                _ => throw new ArgumentException($"Unsupported compression type: {type}")
            };
        }

        /// <summary>압축 타입을 자동 감지합니다.</summary>
        public static ReCompressionType DetectCompressionType(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return ReCompressionType.None;
            }

            // GZip 매직 넘버 체크 (1f 8b)
            if (data[0] == 0x1F && data[1] == 0x8B)
            {
                return ReCompressionType.GZip;
            }

            // LZ4 프레임 매직 넘버 체크 (04 22 4D 18)
            uint magic = BitConverter.ToUInt32(data, 0);

            if (magic == LZ4_MAGIC)
            {
                return ReCompressionType.LZ4;
            }

            return ReCompressionType.None;
        }

        /// <summary>스트림용 압축 스트림을 생성합니다.</summary>
        public static Stream CreateCompressionStream(Stream baseStream, ReCompressionType type, bool leaveOpen = false)
        {
            return type switch
            {
                ReCompressionType.None => baseStream,
                ReCompressionType.GZip => new GZipStream(baseStream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen),
                ReCompressionType.LZ4 => new LZ4CompressionStream(baseStream, leaveOpen),
                _ => throw new ArgumentException($"Unsupported compression type: {type}")
            };
        }

        /// <summary>스트림용 해제 스트림을 생성합니다.</summary>
        public static Stream CreateDecompressionStream(Stream baseStream, ReCompressionType type, bool leaveOpen = false)
        {
            return type switch
            {
                ReCompressionType.None => baseStream,
                ReCompressionType.GZip => new GZipStream(baseStream, System.IO.Compression.CompressionMode.Decompress, leaveOpen),
                ReCompressionType.LZ4 => new LZ4DecompressionStream(baseStream, leaveOpen),
                _ => throw new ArgumentException($"Unsupported compression type: {type}")
            };
        }

        #endregion

        #region GZip Implementation

        private static byte[] CompressGZip(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                {
                    gzip.Write(data, 0, data.Length);
                }
                return output.ToArray();
            }
        }

        private static async Task<byte[]> CompressGZipAsync(byte[] data, CancellationToken ct)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
                {
                    await gzip.WriteAsync(data, 0, data.Length, ct);
                }
                return output.ToArray();
            }
        }

        private static byte[] DecompressGZip(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gzip.CopyTo(output);
                return output.ToArray();
            }
        }

        private static async Task<byte[]> DecompressGZipAsync(byte[] data, CancellationToken ct)
        {
            using (var input = new MemoryStream(data))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                await gzip.CopyToAsync(output, DEFAULT_BUFFER_SIZE, ct);
                return output.ToArray();
            }
        }

        #endregion

        #region LZ4 Implementation (Simple Block Format)

        /// <summary>LZ4 블록 압축 (단순 블록 포맷)</summary>
        private static byte[] CompressLZ4(byte[] data)
        {
            int maxOutputSize = LZ4Codec.MaxCompressedSize(data.Length);
            byte[] output = new byte[maxOutputSize + 8]; // 헤더 공간 추가

            // 헤더: 원본 크기 (4바이트) + 매직 넘버 (4바이트)
            BitConverter.GetBytes(LZ4_MAGIC).CopyTo(output, 0);
            BitConverter.GetBytes(data.Length).CopyTo(output, 4);

            int compressedSize = LZ4Codec.Encode(data, 0, data.Length, output, 8, maxOutputSize);

            if (compressedSize <= 0)
            {
                Debug.LogWarning("[CompressionHelper] LZ4 compression failed, returning uncompressed data");
                return data;
            }

            // 정확한 크기로 배열 조정
            byte[] result = new byte[compressedSize + 8];
            Array.Copy(output, result, result.Length);

            return result;
        }

        /// <summary>LZ4 블록 해제</summary>
        private static byte[] DecompressLZ4(byte[] data)
        {
            if (data.Length < 8)
            {
                throw new InvalidDataException("LZ4 data too short");
            }

            uint magic = BitConverter.ToUInt32(data, 0);

            if (magic != LZ4_MAGIC)
            {
                throw new InvalidDataException("Invalid LZ4 magic number");
            }

            int originalSize = BitConverter.ToInt32(data, 4);

            if (originalSize <= 0 || originalSize > 100 * 1024 * 1024) // 100MB 제한
            {
                throw new InvalidDataException($"Invalid original size: {originalSize}");
            }

            byte[] output = new byte[originalSize];
            int decodedSize = LZ4Codec.Decode(data, 8, data.Length - 8, output, 0, originalSize);

            if (decodedSize != originalSize)
            {
                throw new InvalidDataException($"LZ4 decompression size mismatch: expected {originalSize}, got {decodedSize}");
            }

            return output;
        }

        #endregion
    }

    /// <summary>LZ4 인코더/디코더 (순수 C# 구현)</summary>
    internal static class LZ4Codec
    {
        private const int HASH_LOG = 12;
        private const int HASH_TABLE_SIZE = 1 << HASH_LOG;
        private const int MIN_MATCH = 4;
        private const int MAX_INPUT_SIZE = 0x7E000000;
        private const int COPY_LENGTH = 8;
        private const int ML_BITS = 4;
        private const int ML_MASK = (1 << ML_BITS) - 1;
        private const int RUN_BITS = 8 - ML_BITS;
        private const int RUN_MASK = (1 << RUN_BITS) - 1;

        /// <summary>최대 압축 크기 계산</summary>
        public static int MaxCompressedSize(int inputSize)
        {
            return inputSize + (inputSize / 255) + 16;
        }

        /// <summary>LZ4 압축</summary>
        public static int Encode(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength)
        {
            if (inputLength == 0)
            {
                return 0;
            }

            if (inputLength > MAX_INPUT_SIZE)
            {
                throw new ArgumentException("Input too large for LZ4");
            }

            int[] hashTable = new int[HASH_TABLE_SIZE];

            for (int i = 0; i < HASH_TABLE_SIZE; i++)
            {
                hashTable[i] = -1;
            }

            int inputEnd = inputOffset + inputLength;
            int outputEnd = outputOffset + outputLength;
            int anchor = inputOffset;
            int ip = inputOffset;
            int op = outputOffset;

            int matchLimit = inputEnd - 12; // LASTLITERALS
            int limitEnd = inputEnd - 5;    // MFLIMIT

            if (inputLength < 13) // LZ4_minLength
            {
                goto _lastLiterals;
            }

            // 첫 번째 바이트 처리
            hashTable[Hash(input, ip)] = ip;
            ip++;

            // 메인 루프
            while (ip < matchLimit)
            {
                int refPos = hashTable[Hash(input, ip)];
                int distance = ip - refPos;

                hashTable[Hash(input, ip)] = ip;

                if (distance > 0 && distance < 65536 && refPos >= inputOffset && Match4(input, refPos, ip))
                {
                    // 매치 발견
                    int literalLength = ip - anchor;

                    // 토큰 위치
                    int token = op++;

                    // 리터럴 길이 인코딩
                    if (literalLength >= RUN_MASK)
                    {
                        output[token] = (byte)(RUN_MASK << ML_BITS);
                        int len = literalLength - RUN_MASK;

                        while (len >= 255)
                        {
                            output[op++] = 255;
                            len -= 255;
                        }

                        output[op++] = (byte)len;
                    }
                    else
                    {
                        output[token] = (byte)(literalLength << ML_BITS);
                    }

                    // 리터럴 복사
                    Buffer.BlockCopy(input, anchor, output, op, literalLength);
                    op += literalLength;

                    // 오프셋 저장
                    output[op++] = (byte)distance;
                    output[op++] = (byte)(distance >> 8);

                    // 매치 길이 계산
                    int matchRef = refPos + MIN_MATCH;
                    int matchIp = ip + MIN_MATCH;

                    while (matchIp < limitEnd && input[matchRef] == input[matchIp])
                    {
                        matchRef++;
                        matchIp++;
                    }

                    int matchLength = matchIp - ip - MIN_MATCH;

                    // 매치 길이 인코딩
                    if (matchLength >= ML_MASK)
                    {
                        output[token] |= ML_MASK;
                        int len = matchLength - ML_MASK;

                        while (len >= 255)
                        {
                            output[op++] = 255;
                            len -= 255;
                        }

                        output[op++] = (byte)len;
                    }
                    else
                    {
                        output[token] |= (byte)matchLength;
                    }

                    ip = matchIp;
                    anchor = ip;

                    if (ip >= matchLimit)
                    {
                        break;
                    }

                    hashTable[Hash(input, ip - 2)] = ip - 2;
                    hashTable[Hash(input, ip)] = ip;
                    ip++;
                }
                else
                {
                    ip++;
                }
            }

        _lastLiterals:
            // 마지막 리터럴
            int lastLiteralLength = inputEnd - anchor;

            if (op + lastLiteralLength + 1 + (lastLiteralLength + 255 - RUN_MASK) / 255 > outputEnd)
            {
                return 0; // 출력 버퍼 부족
            }

            if (lastLiteralLength >= RUN_MASK)
            {
                output[op++] = (byte)(RUN_MASK << ML_BITS);
                int len = lastLiteralLength - RUN_MASK;

                while (len >= 255)
                {
                    output[op++] = 255;
                    len -= 255;
                }

                output[op++] = (byte)len;
            }
            else
            {
                output[op++] = (byte)(lastLiteralLength << ML_BITS);
            }

            Buffer.BlockCopy(input, anchor, output, op, lastLiteralLength);
            op += lastLiteralLength;

            return op - outputOffset;
        }

        /// <summary>LZ4 해제</summary>
        public static int Decode(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength)
        {
            int inputEnd = inputOffset + inputLength;
            int outputEnd = outputOffset + outputLength;
            int ip = inputOffset;
            int op = outputOffset;

            while (ip < inputEnd)
            {
                int token = input[ip++];

                // 리터럴 길이
                int literalLength = token >> ML_BITS;

                if (literalLength == RUN_MASK)
                {
                    int s;

                    do
                    {
                        s = input[ip++];
                        literalLength += s;
                    } while (s == 255);
                }

                // 리터럴 복사
                if (op + literalLength > outputEnd)
                {
                    throw new InvalidDataException("LZ4 output buffer overflow during literal copy");
                }

                Buffer.BlockCopy(input, ip, output, op, literalLength);
                ip += literalLength;
                op += literalLength;

                // 마지막 리터럴이면 종료
                if (ip >= inputEnd)
                {
                    break;
                }

                // 오프셋 읽기
                int offset = input[ip++] | (input[ip++] << 8);

                if (offset == 0)
                {
                    throw new InvalidDataException("LZ4 invalid offset");
                }

                int matchPos = op - offset;

                if (matchPos < outputOffset)
                {
                    throw new InvalidDataException("LZ4 match position before start");
                }

                // 매치 길이
                int matchLength = (token & ML_MASK) + MIN_MATCH;

                if ((token & ML_MASK) == ML_MASK)
                {
                    int s;

                    do
                    {
                        s = input[ip++];
                        matchLength += s;
                    } while (s == 255);
                }

                // 매치 복사 (오버랩 가능)
                if (op + matchLength > outputEnd)
                {
                    throw new InvalidDataException("LZ4 output buffer overflow during match copy");
                }

                // 바이트 단위 복사 (오버랩 처리)
                for (int i = 0; i < matchLength; i++)
                {
                    output[op++] = output[matchPos++];
                }
            }

            return op - outputOffset;
        }

        private static int Hash(byte[] data, int offset)
        {
            uint value = BitConverter.ToUInt32(data, offset);
            return (int)((value * 2654435761u) >> (32 - HASH_LOG));
        }

        private static bool Match4(byte[] data, int pos1, int pos2)
        {
            return data[pos1] == data[pos2]
                && data[pos1 + 1] == data[pos2 + 1]
                && data[pos1 + 2] == data[pos2 + 2]
                && data[pos1 + 3] == data[pos2 + 3];
        }
    }

    /// <summary>LZ4 압축 스트림</summary>
    internal class LZ4CompressionStream : Stream
    {
        private readonly Stream baseStream;
        private readonly bool leaveOpen;
        private readonly MemoryStream buffer;
        private bool disposed;

        public LZ4CompressionStream(Stream baseStream, bool leaveOpen)
        {
            this.baseStream = baseStream;
            this.leaveOpen = leaveOpen;
            this.buffer = new MemoryStream();
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => buffer.Length;
        public override long Position
        {
            get => buffer.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            // 버퍼의 데이터를 압축하여 기본 스트림에 쓰기
            if (buffer.Length > 0)
            {
                byte[] data = buffer.ToArray();
                byte[] compressed = CompressionHelper.Compress(data, ReCompressionType.LZ4);
                baseStream.Write(compressed, 0, compressed.Length);
                buffer.SetLength(0);
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.buffer.Write(buffer, offset, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    Flush();
                    buffer.Dispose();

                    if (!leaveOpen)
                    {
                        baseStream.Dispose();
                    }
                }
                disposed = true;
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>LZ4 해제 스트림</summary>
    internal class LZ4DecompressionStream : Stream
    {
        private readonly Stream baseStream;
        private readonly bool leaveOpen;
        private byte[] decompressedData;
        private int position;
        private bool disposed;

        public LZ4DecompressionStream(Stream baseStream, bool leaveOpen)
        {
            this.baseStream = baseStream;
            this.leaveOpen = leaveOpen;
            LoadAndDecompress();
        }

        private void LoadAndDecompress()
        {
            using (var ms = new MemoryStream())
            {
                baseStream.CopyTo(ms);
                byte[] compressed = ms.ToArray();
                decompressedData = CompressionHelper.Decompress(compressed, ReCompressionType.LZ4);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => decompressedData?.Length ?? 0;
        public override long Position
        {
            get => position;
            set => position = (int)value;
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (decompressedData == null)
            {
                return 0;
            }

            int available = decompressedData.Length - position;
            int toRead = Math.Min(count, available);

            if (toRead <= 0)
            {
                return 0;
            }

            Array.Copy(decompressedData, position, buffer, offset, toRead);
            position += toRead;
            return toRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    position = (int)offset;
                    break;
                case SeekOrigin.Current:
                    position += (int)offset;
                    break;
                case SeekOrigin.End:
                    position = decompressedData.Length + (int)offset;
                    break;
            }
            return position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    if (!leaveOpen)
                    {
                        baseStream.Dispose();
                    }
                }
                disposed = true;
            }
            base.Dispose(disposing);
        }
    }
}
