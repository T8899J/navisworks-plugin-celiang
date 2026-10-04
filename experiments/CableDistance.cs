using System;
using System.Numerics;
using JiePinPai.TrayMeasurement.Core;

namespace TrayRouteExperiment
{
    // Exact sums of non-negative IEEE double edge weights, in units of 2^-1074.
    // This preserves length ordering without an epsilon and makes route costs independent
    // of traversal direction. Convert back to double only when publishing metre values.
    internal struct CableDistance : IComparable<CableDistance>
    {
        readonly BigInteger units;
        CableDistance(BigInteger units) { this.units=units; }
        public static CableDistance FromMetres(double value)
        {
            if(!Vec.IsFinite(value)||value<0)throw new ArgumentOutOfRangeException("value","Path distances must be finite and non-negative.");
            ulong bits=(ulong)BitConverter.DoubleToInt64Bits(value);
            int exponent=(int)((bits>>52)&2047);ulong fraction=bits&0xFFFFFFFFFFFFFUL;
            return new CableDistance(exponent==0?new BigInteger(fraction):new BigInteger(fraction|0x10000000000000UL)<<(exponent-1));
        }
        public static CableDistance operator +(CableDistance a,CableDistance b) { return new CableDistance(a.units+b.units); }
        public int CompareTo(CableDistance other) { return units.CompareTo(other.units); }
        public double Metres
        {
            get
            {
                if(units.IsZero)return 0;
                var bytes=units.ToByteArray();int last=bytes.Length-1;while(last>0&&bytes[last]==0)last--;
                int bitLength=last*8;byte top=bytes[last];while(top!=0){bitLength++;top>>=1;}
                if(bitLength<=52)return BitConverter.Int64BitsToDouble((long)units);
                int shift=bitLength-53;var mantissa=units>>shift;
                if(shift>0)
                {
                    var remainder=units-(mantissa<<shift);var halfway=BigInteger.One<<(shift-1);
                    int comparison=remainder.CompareTo(halfway);
                    if(comparison>0||(comparison==0&&!mantissa.IsEven))mantissa++;
                    if(mantissa==(BigInteger.One<<53)){mantissa>>=1;shift++;}
                }
                int exponent=shift+1;if(exponent>=2047)return double.PositiveInfinity;
                long bits=((long)exponent<<52)|(long)(mantissa-(BigInteger.One<<52));
                return BitConverter.Int64BitsToDouble(bits);
            }
        }
        public static CableDistance VerticalTravel(Vec[] line)
        {
            var result=new CableDistance();
            if(line!=null)for(int i=1;i<line.Length;i++)result+=FromMetres(Math.Abs(line[i].Z-line[i-1].Z));
            return result;
        }
    }
}
