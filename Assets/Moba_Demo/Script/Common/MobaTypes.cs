using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MobaTypes
{
    public enum EUsingType
    {
        Instance = 0, Chanelling, Charge, Toggle
    }

    public enum ETargetType
    {
        Targeting = 0, NonTargeting, Self,
    }
    public enum EStatusMutiply
    {
        HP, PhysicDmg, CastingDmg, Mana, Armor,
    }

    public enum EStateTeam
    {
        Neutral, Team , Opponent,
    }

    [System.Serializable]
    public struct DamageCalculateInfo
    {
        public int BaseDamge;
        public float MutiplyValue;
        public EStatusMutiply MutiplyType;
    }

    [System.Serializable]
    public struct CharacterStatus
    {
        public int nHP;
        public int nDamage;
        public int nSkillDamage;
        public int nReduceCoolTime;
        public int nAttackSpeed;
        public int nMovementSpeed;
        public int nCriticalPer;
        public int nCriticalDmg;
        public int nRange;
    }
    [System.Serializable]
    public struct SkillDataPerLevel
    {
        DamageCalculateInfo DamageData;
        public int nCoolDown;

    }

    [System.Serializable]
    public struct SkillData
    {
        public Sprite icon;
        public EUsingType UsingType;
        public ETargetType TargetType;
        public List<DamageCalculateInfo> DamageData;
    }

    // 클라↔서버 공용 패킷 ID (순서 바꾸면 깨짐)
    public enum PacketType
    {
        None,
        UserLoginReq,
        UserLoginResult,
        UserDuplicateLogin,
        UserRegistReq,
        UserRegistResult,
        UserChat,
        UserMatchingReq,
        UserMatchingStatus,
        UserMatchingResult,
        UserCharacterSelectReq,
        UserCharacterSelectSync,
        UserGameStart,
        UserPositionReq,
        UserPositionSync,
        UserLeaveRoomReq
    }

    public enum SessionState
    {
        None = 0,
        Disconnected,
        LoggedIn,
        InGame,
    }

    struct PacketHeader
    {
        int Length;
        byte PacketType;
    }

    // 바이너리 직렬화 — string은 [int len][utf8 bytes], int/float LE
    public class Packet : IDisposable
    {
        public PacketType Type;
        private byte[] _buffer;
        private int _writePos;
        private int _readPos;

        public int Length => _writePos;

        public Packet(int initialSize = 256)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialSize);
            _writePos = 0;
            _readPos = 0;
        }

        public void SetData(byte[] data, int length, bool fromPool = false)
        {
            if (fromPool)
            {
                // 외부 ArrayPool 배열 소유권 가져오기
                _buffer = data;
            }
            else
            {
                // 안전하게 자체 복사
                _buffer = ArrayPool<byte>.Shared.Rent(length);
                Buffer.BlockCopy(data, 0, _buffer, 0, length);
            }

            _writePos = length;
            _readPos = 0;
        }
        public void SetReadPos(int pos)
        {
            if (pos < 0 || pos > _writePos)
                throw new ArgumentOutOfRangeException(nameof(pos));
            _readPos = pos;
        }

        // ------------------- Writer -------------------
        private void Ensure(int size)
        {
            if (_buffer == null)
                _buffer = ArrayPool<byte>.Shared.Rent(256);

            if (_writePos + size <= _buffer.Length) return;

            int newSize = Math.Max(_buffer.Length * 2, _writePos + size);
            byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _writePos);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }

        public void Write(int value)
        {
            Ensure(4);
            BitConverter.TryWriteBytes(new Span<byte>(_buffer, _writePos, 4), value);
            _writePos += 4;
        }

        public void Write(float value)
        {
            Ensure(4);
            BitConverter.TryWriteBytes(new Span<byte>(_buffer, _writePos, 4), value);
            _writePos += 4;
        }

        public void Write(bool value)
        {
            Ensure(1);
            _buffer[_writePos++] = (byte)(value ? 1 : 0);
        }

        public void Write(string value)
        {
            byte[] strBytes = Encoding.UTF8.GetBytes(value);
            Write(strBytes.Length);
            Ensure(strBytes.Length);
            Buffer.BlockCopy(strBytes, 0, _buffer, _writePos, strBytes.Length);
            _writePos += strBytes.Length;
        }

        public ArraySegment<byte> Data
        {
            get
            {
                if (_buffer == null)
                    return default;
                return new ArraySegment<byte>(_buffer, 0, _writePos);
            }
        }

        public bool TryGetPayload(out byte[] buffer, out int length)
        {
            buffer = _buffer;
            length = _writePos;
            return _buffer != null;
        }

        public void PrepareForReuse(int initialSize = 256)
        {
            Type = default;
            _writePos = 0;
            _readPos = 0;
            if (_buffer == null)
                _buffer = ArrayPool<byte>.Shared.Rent(initialSize);
        }

        // ------------------- Reader -------------------

        public float ReadFloat()
        {

            if (_buffer == null || _readPos + 4 > _writePos)
                throw new InvalidOperationException("Packet read out of bounds");
            float value = BitConverter.ToSingle(_buffer, _readPos);
            _readPos += 4;
            return value;
        }

        public bool ReadBool()
        {
            bool value = _buffer[_readPos] == 1;
            _readPos += 1;
            return value;
        }
        public int ReadInt()
        {
            if (_buffer == null || _readPos + 4 > _writePos)
                throw new InvalidOperationException("Packet read out of bounds");
            int value = BitConverter.ToInt32(_buffer, _readPos);
            _readPos += 4;
            return value;
        }

        public string ReadString()
        {
            int len = ReadInt();
            if (_buffer == null || _readPos + len > _writePos)
                throw new InvalidOperationException("Packet read out of bounds");
            string str = Encoding.UTF8.GetString(_buffer, _readPos, len);
            _readPos += len;
            return str;
        }

        public void Reset()
        {
            Type = default;
            _writePos = 0;
            _readPos = 0;
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }

        public void Dispose() => Reset();
    }

    public class UserData
    {
        int Uid;
        int Money = 0;
        int MMR = 0;
        string NickName;
        private HashSet<int> ownedSkins;

        public bool DirtyFlag { get; private set; } // 서버만 — SaveAllDirtyUsers 때 DB 반영

        public UserData() { ownedSkins = new HashSet<int>(); }
        public bool HasSkin(int id) { return ownedSkins.Contains(id); }
        public int GetUid() => Uid;
        public void SetUid(int uid) => Uid = uid;
        public int GetMoney() => Money;
        public int GetMMR() => MMR;
        public void SetMMR(int mMR) => MMR = mMR;
        public void SetMoney(int money) => Money = money;
        public void SetNickName(string nickName) => NickName = nickName;

        public string GetUserNickName() => NickName;

        public void ChangeMoney(int amount)
        {
            Money += amount;
            DirtyFlag = true;
        }

        public void AddSkin(int id)
        {
            ownedSkins.Add(id);
            DirtyFlag = true;
        }

        public void ChangeMMR(int mMr)
        {
            MMR += mMr;
            DirtyFlag = true;
        }

        public void MarkClean() => DirtyFlag = false;
    }

    public enum GameType
    {
        Practice,
        OneVsOne,
        FiveVsFive,
        FreeForAll
    }

    public enum EMatchingState
    {
        MatchingWait = 0,
        MatchingDone = 1
    }

    public enum GameState
    {
        None, CharacterSelect, Opening, MiddleGame, EndGame
    }
    public enum PlayableCharacter
    { 
        None,Gunner,Assassin
    }
}
