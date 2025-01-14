﻿using System.Collections.Generic;
using System.IO;

namespace SharpCompress.Common.Rar;

internal sealed class RarCryptoBinaryReader : RarCrcBinaryReader
{
    private RarRijndael _rijndael;
    private byte[] _salt;
    private readonly string _password;
    private readonly Queue<byte> _data = new Queue<byte>();
    private long _readCount;

    public RarCryptoBinaryReader(Stream stream, string password) : base(stream)
    {
        _password = password;

        // coderb: not sure why this was being done at this logical point
        //SkipQueue();
        var salt = ReadBytes(8);

        _salt = salt;
        _rijndael = RarRijndael.InitializeFrom(_password, salt);
    }

    // track read count ourselves rather than using the underlying stream since we buffer
    public override long CurrentReadByteCount
    {
        get => _readCount;
        protected set
        {
            // ignore
        }
    }

    public override void Mark() => _readCount = 0;

    private bool UseEncryption => _salt != null;

    public override byte ReadByte()
    {
        if (UseEncryption)
        {
            return ReadAndDecryptBytes(1)[0];
        }

        _readCount++;
        return base.ReadByte();
    }

    public override byte[] ReadBytes(int count)
    {
        if (UseEncryption)
        {
            return ReadAndDecryptBytes(count);
        }

        _readCount += count;
        return base.ReadBytes(count);
    }

    private byte[] ReadAndDecryptBytes(int count)
    {
        var queueSize = _data.Count;
        var sizeToRead = count - queueSize;

        if (sizeToRead > 0)
        {
            var alignedSize = sizeToRead + ((~sizeToRead + 1) & 0xf);
            for (var i = 0; i < alignedSize / 16; i++)
            {
                //long ax = System.currentTimeMillis();
                var cipherText = ReadBytesNoCrc(16);
                var readBytes = _rijndael.ProcessBlock(cipherText);
                foreach (var readByte in readBytes)
                {
                    _data.Enqueue(readByte);
                }
            }
        }

        var decryptedBytes = new byte[count];

        for (var i = 0; i < count; i++)
        {
            var b = _data.Dequeue();
            decryptedBytes[i] = b;
            UpdateCrc(b);
        }

        _readCount += count;
        return decryptedBytes;
    }

    public void ClearQueue() => _data.Clear();

    public void SkipQueue()
    {
        var position = BaseStream.Position;
        BaseStream.Position = position + _data.Count;
        ClearQueue();
    }
}
