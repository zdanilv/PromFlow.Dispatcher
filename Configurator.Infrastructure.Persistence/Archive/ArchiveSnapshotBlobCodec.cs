using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Configurator.Application.Services.Archiving;

namespace Configurator.Infrastructure.Persistence.Archive;

public sealed class ArchiveSnapshotBlobCodec
{
    public const ushort CodecVersion = 1;

    private const int HeaderLength = 12;
    private const string ArchiveSnapshotBlobInvalidMagic = nameof(ArchiveSnapshotBlobInvalidMagic);
    private const string ArchiveSnapshotBlobUnsupportedVersion = nameof(ArchiveSnapshotBlobUnsupportedVersion);
    private const string ArchiveSnapshotBlobUnsupportedHeader = nameof(ArchiveSnapshotBlobUnsupportedHeader);
    private const string ArchiveSnapshotBlobUnsupportedItemType = nameof(ArchiveSnapshotBlobUnsupportedItemType);
    private const string ArchiveSnapshotBlobInvalidCount = nameof(ArchiveSnapshotBlobInvalidCount);
    private const string ArchiveSnapshotBlobTruncated = nameof(ArchiveSnapshotBlobTruncated);
    private const string ArchiveSnapshotBlobCountMismatch = nameof(ArchiveSnapshotBlobCountMismatch);
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PFS1");

    public byte[] EncodeCoils(IReadOnlyList<bool> coils)
    {
        ArgumentNullException.ThrowIfNull(coils);

        var payloadLength = (coils.Count + 7) / 8;
        var blob = CreateBlob(ArchiveSnapshotBlobItemType.Coils, coils.Count, payloadLength);

        for (var index = 0; index < coils.Count; index++)
        {
            if (coils[index])
            {
                blob[HeaderLength + (index / 8)] |= (byte)(1 << (index % 8));
            }
        }

        return blob;
    }

    public ArchiveOperationResult<IReadOnlyList<bool>> DecodeCoils(ReadOnlySpan<byte> blob)
    {
        var headerResult = DecodeHeader(blob, ArchiveSnapshotBlobItemType.Coils);
        if (!headerResult.Succeeded)
        {
            return Failure<IReadOnlyList<bool>>(headerResult);
        }

        var header = headerResult.Value!;
        var expectedPayloadLength = (header.ItemCount + 7) / 8;
        var lengthResult = ValidatePayloadLength(blob, expectedPayloadLength);
        if (!lengthResult.Succeeded)
        {
            return Failure<IReadOnlyList<bool>>(lengthResult);
        }

        if (header.ItemCount > 0 && header.ItemCount % 8 != 0)
        {
            var usedBitsInLastByte = header.ItemCount % 8;
            var unusedMask = (byte)(0xFF << usedBitsInLastByte);
            var lastByte = blob[HeaderLength + expectedPayloadLength - 1];
            if ((lastByte & unusedMask) != 0)
            {
                return ArchiveOperationResult<IReadOnlyList<bool>>.Failure(
                    ArchiveSnapshotBlobCountMismatch,
                    "Coil payload contains set bits outside the item count.");
            }
        }

        var coils = new bool[header.ItemCount];
        for (var index = 0; index < coils.Length; index++)
        {
            var value = blob[HeaderLength + (index / 8)];
            coils[index] = (value & (1 << (index % 8))) != 0;
        }

        return ArchiveOperationResult<IReadOnlyList<bool>>.Success(Array.AsReadOnly(coils));
    }

    public byte[] EncodeHoldingRegisters(IReadOnlyList<ushort> registers)
    {
        ArgumentNullException.ThrowIfNull(registers);

        var payloadLength = checked(registers.Count * 2);
        var blob = CreateBlob(ArchiveSnapshotBlobItemType.HoldingRegisters, registers.Count, payloadLength);
        var payload = blob.AsSpan(HeaderLength, payloadLength);

        for (var index = 0; index < registers.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.Slice(index * 2, 2), registers[index]);
        }

        return blob;
    }

    public ArchiveOperationResult<IReadOnlyList<ushort>> DecodeHoldingRegisters(ReadOnlySpan<byte> blob)
    {
        var headerResult = DecodeHeader(blob, ArchiveSnapshotBlobItemType.HoldingRegisters);
        if (!headerResult.Succeeded)
        {
            return Failure<IReadOnlyList<ushort>>(headerResult);
        }

        var header = headerResult.Value!;
        if (header.ItemCount > (int.MaxValue - HeaderLength) / 2)
        {
            return ArchiveOperationResult<IReadOnlyList<ushort>>.Failure(
                ArchiveSnapshotBlobInvalidCount,
                "Register item count is too large.");
        }

        var expectedPayloadLength = header.ItemCount * 2;
        var lengthResult = ValidatePayloadLength(blob, expectedPayloadLength);
        if (!lengthResult.Succeeded)
        {
            return Failure<IReadOnlyList<ushort>>(lengthResult);
        }

        var registers = new ushort[header.ItemCount];
        var payload = blob[HeaderLength..];
        for (var index = 0; index < registers.Length; index++)
        {
            registers[index] = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(index * 2, 2));
        }

        return ArchiveOperationResult<IReadOnlyList<ushort>>.Success(Array.AsReadOnly(registers));
    }

    private static byte[] CreateBlob(ArchiveSnapshotBlobItemType itemType, int itemCount, int payloadLength)
    {
        var blob = new byte[HeaderLength + payloadLength];
        Magic.AsSpan().CopyTo(blob.AsSpan(0, Magic.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(4, 2), CodecVersion);
        blob[6] = (byte)itemType;
        blob[7] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(8, 4), itemCount);

        return blob;
    }

    private static ArchiveOperationResult<BlobHeader> DecodeHeader(
        ReadOnlySpan<byte> blob,
        ArchiveSnapshotBlobItemType expectedItemType)
    {
        if (blob.Length < HeaderLength)
        {
            return ArchiveOperationResult<BlobHeader>.Failure(
                ArchiveSnapshotBlobTruncated,
                "Snapshot blob header is truncated.");
        }

        if (!blob[..Magic.Length].SequenceEqual(Magic))
        {
            return ArchiveOperationResult<BlobHeader>.Failure(
                ArchiveSnapshotBlobInvalidMagic,
                "Snapshot blob magic is invalid.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(blob.Slice(4, 2));
        if (version != CodecVersion)
        {
            return ArchiveOperationResult<BlobHeader>.Failure(
                ArchiveSnapshotBlobUnsupportedVersion,
                "Snapshot blob codec version is unsupported.",
                version.ToString(CultureInfo.InvariantCulture));
        }

        if (blob[7] != 0)
        {
            return ArchiveOperationResult<BlobHeader>.Failure(
                ArchiveSnapshotBlobUnsupportedHeader,
                "Snapshot blob reserved header byte must be zero.");
        }

        var itemType = (ArchiveSnapshotBlobItemType)blob[6];
        if (!Enum.IsDefined(itemType) || itemType != expectedItemType)
        {
            return ArchiveOperationResult<BlobHeader>.Failure(
                ArchiveSnapshotBlobUnsupportedItemType,
                "Snapshot blob item type is unsupported.",
                blob[6].ToString(CultureInfo.InvariantCulture));
        }

        var itemCount = BinaryPrimitives.ReadInt32LittleEndian(blob.Slice(8, 4));
        if (itemCount < 0)
        {
            return ArchiveOperationResult<BlobHeader>.Failure(
                ArchiveSnapshotBlobInvalidCount,
                "Snapshot blob item count is invalid.",
                itemCount.ToString(CultureInfo.InvariantCulture));
        }

        return ArchiveOperationResult<BlobHeader>.Success(new BlobHeader(itemType, itemCount));
    }

    private static ArchiveOperationResult ValidatePayloadLength(ReadOnlySpan<byte> blob, int expectedPayloadLength)
    {
        var expectedLength = HeaderLength + expectedPayloadLength;
        if (blob.Length < expectedLength)
        {
            return ArchiveOperationResult.Failure(
                ArchiveSnapshotBlobTruncated,
                "Snapshot blob payload is truncated.");
        }

        if (blob.Length != expectedLength)
        {
            return ArchiveOperationResult.Failure(
                ArchiveSnapshotBlobCountMismatch,
                "Snapshot blob payload length does not match item count.");
        }

        return ArchiveOperationResult.Success();
    }

    private static ArchiveOperationResult<T> Failure<T>(ArchiveOperationResult result)
        => ArchiveOperationResult<T>.Failure(
            result.ErrorCode ?? ArchiveSnapshotBlobUnsupportedHeader,
            result.ErrorMessage ?? "Snapshot blob is invalid.",
            result.ErrorDetails);

    private static ArchiveOperationResult<T> Failure<T>(ArchiveOperationResult<BlobHeader> result)
        => ArchiveOperationResult<T>.Failure(
            result.ErrorCode ?? ArchiveSnapshotBlobUnsupportedHeader,
            result.ErrorMessage ?? "Snapshot blob is invalid.",
            result.ErrorDetails);

    private sealed record BlobHeader(ArchiveSnapshotBlobItemType ItemType, int ItemCount);
}
