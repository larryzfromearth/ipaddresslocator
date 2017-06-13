﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace IPAddressLocater
{
    public class IPLocater
    {
        private readonly byte[] _data;
        private readonly long _firstStartIpOffset; //索引区第一条流位置
        private readonly long _lastStartIpOffset; //索引区最后一条流位置
        private readonly long _prefixCount; //前缀数量
        private readonly Dictionary<uint, PrefixIndex> _prefixDict;
        private readonly long _prefixEndOffset; //前缀区最后一条的流位置
        private readonly long _prefixStartOffset; //前缀区第一条的流位置

        /// <summary>
        ///     初始化二进制dat数据
        /// </summary>
        /// <param name="dataPath"></param>
        public IPLocater(string dataPath)
        {
            using (var fs = new FileStream(dataPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                _data = new byte[fs.Length];
                fs.Read(_data, 0, _data.Length);
            }

            _firstStartIpOffset = BytesToLong(_data[0], _data[1], _data[2], _data[3]);
            _lastStartIpOffset = BytesToLong(_data[4], _data[5], _data[6], _data[7]);
            _prefixStartOffset = BytesToLong(_data[8], _data[9], _data[10], _data[11]);
            _prefixEndOffset = BytesToLong(_data[12], _data[13], _data[14], _data[15]);

            //prefixCount 不固定为256 方便以后自由定制 国内版  国外版 全球版 或者某部分 都可以

            IPCount = (_lastStartIpOffset - _firstStartIpOffset) / 12 + 1; //索引区块每组 12字节          
            _prefixCount = (_prefixEndOffset - _prefixStartOffset) / 9 + 1; //前缀区块每组 9字节

            //初始化前缀对应索引区区间
            var indexBuffer = new byte[_prefixCount * 9];
            Array.Copy(_data, _prefixStartOffset, indexBuffer, 0, _prefixCount * 9);
            _prefixDict = new Dictionary<uint, PrefixIndex>();
            for (var k = 0; k < _prefixCount; k++)
            {
                var i = k * 9;
                uint prefix = indexBuffer[i];
                long startIndex = BytesToLong(indexBuffer[i + 1], indexBuffer[i + 2], indexBuffer[i + 3],
                    indexBuffer[i + 4]);
                long endIndex = BytesToLong(indexBuffer[i + 5], indexBuffer[i + 6], indexBuffer[i + 7],
                    indexBuffer[i + 8]);
                _prefixDict.Add(prefix,
                    new PrefixIndex {Prefix = prefix, StartIndex = startIndex, EndIndex = endIndex});
            }
        }

        /// <summary>
        ///     获取IP段数量
        /// </summary>
        public long IPCount { get; }

        public static uint IpToInt(string ip, out uint prefix)
        {
            var bytes = IPAddress.Parse(ip).GetAddressBytes();
            prefix = bytes[0];
            return bytes[3] + ((uint) bytes[2] << 8) + ((uint) bytes[1] << 16) + ((uint) bytes[0] << 24);
        }

        public static string IntToIP(uint ipInt)
        {
            return new IPAddress(ipInt).ToString();
        }

        /// <summary>
        ///     根据ip查询多维字段信息
        /// </summary>
        /// <param name="ip">ip地址（123.4.5.6）</param>
        /// <returns>亚洲|中国|香港|九龙|油尖旺|新世界电讯|810200|Hong Kong|HK|114.17495|22.327115</returns>
        public string Query(string ip)
        {
            uint ipPrefixValue;
            var intIP = IpToInt(ip, out ipPrefixValue);
            uint high = 0;
            uint low = 0;
            uint startIp = 0;
            uint endIp = 0;
            uint localOffset = 0;
            uint localLength = 0;


            if (_prefixDict.ContainsKey(ipPrefixValue))
            {
                low = (uint) _prefixDict[ipPrefixValue].StartIndex;
                high = (uint) _prefixDict[ipPrefixValue].EndIndex;
            }
            else
            {
                return string.Empty;
            }

            var myIndex = low == high ? low : BinarySearch(low, high, intIP);

            GetIndex(myIndex, out startIp, out endIp, out localOffset, out localLength);

            if (startIp <= intIP && endIp >= intIP)
            {
                return GetLocal(localOffset, localLength);
            }
            return string.Empty;
        }

        /// <summary>
        ///     二分逼近算法
        /// </summary>
        public uint BinarySearch(uint low, uint high, uint k)
        {
            uint m = 0;
            while (low <= high)
            {
                var mid = (low + high) / 2;

                var endipNum = GetEndIp(mid);
                if (endipNum >= k)
                {
                    m = mid;
                    if (mid == 0)
                    {
                        break; //防止溢出
                    }
                    high = mid - 1;
                }
                else
                {
                    low = mid + 1;
                }
            }
            return m;
        }

        /// <summary>
        ///     在索引区解析
        /// </summary>
        /// <param name="left">ip第left个索引</param>
        /// <param name="startip">返回开始ip的数值</param>
        /// <param name="endip">返回结束ip的数值</param>
        /// <param name="localOffset">返回地址信息的流位置</param>
        /// <param name="localLength">返回地址信息的流长度</param>
        private void GetIndex(uint left, out uint startip, out uint endip, out uint localOffset, out uint localLength)
        {
            var leftOffset = _firstStartIpOffset + left * 12;
            startip = BytesToLong(_data[leftOffset], _data[1 + leftOffset], _data[2 + leftOffset],
                _data[3 + leftOffset]);
            endip = BytesToLong(_data[4 + leftOffset], _data[5 + leftOffset], _data[6 + leftOffset],
                _data[7 + leftOffset]);
            localOffset = _data[8 + leftOffset] + ((uint) _data[9 + leftOffset] << 8) +
                          ((uint) _data[10 + leftOffset] << 16);
            localLength = _data[11 + leftOffset];
        }

        /// <summary>
        ///     只获取结束ip的数值
        /// </summary>
        /// <param name="left">索引区第left个索引</param>
        /// <returns>返回结束ip的数值</returns>
        private uint GetEndIp(uint left)
        {
            var leftOffset = _firstStartIpOffset + left * 12;
            return BytesToLong(_data[4 + leftOffset], _data[5 + leftOffset], _data[6 + leftOffset],
                _data[7 + leftOffset]);
        }

        /// <summary>
        ///     返回地址信息
        /// </summary>
        /// <param name="localOffset">地址信息的流位置</param>
        /// <param name="localLength">地址信息的流长度</param>
        /// <returns></returns>
        private string GetLocal(uint localOffset, uint localLength)
        {
            var buf = new byte[localLength];
            Array.Copy(_data, localOffset, buf, 0, localLength);
            return Encoding.UTF8.GetString(buf, 0, (int) localLength);
        }

        /// <summary>
        ///     字节转整形 小节序
        /// </summary>
        private uint BytesToLong(byte a, byte b, byte c, byte d)
        {
            return ((uint) a << 0) | ((uint) b << 8) | ((uint) c << 16) | ((uint) d << 24);
        }
    }

    /*
    （调用例子）：
    IPLocater finder = new IPLocater("IPLocater.dat");
    string result = finder.Query("202.102.227.68");
   --> result="CN|中国|400000|华中|410000|河南省|410300|洛阳市|||100026|联通"
    */
    public class PrefixIndex
    {
        public uint Prefix { get; set; }
        public long StartIndex { get; set; }
        public long EndIndex { get; set; }
    }
}