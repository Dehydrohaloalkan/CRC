# CRC-32 — Algorithm Mathematics

## What is CRC?

CRC (Cyclic Redundancy Check) treats a byte stream as a polynomial over the field GF(2) —
binary coefficients where addition and subtraction both equal XOR (no carries).
The checksum is the remainder when the message polynomial is divided by a fixed generator
polynomial G(x) of degree 32.

The generator used here is:

```
G(x) = x^32 + x^26 + x^23 + x^22 + x^16 + x^12 + x^11 + x^10
             + x^8  + x^7  + x^5  + x^4  + x^2  + x + 1
```

## Reflected (LSB-first) representation

The implementation is *reflected*: bits are processed least-significant-first.
The register shifts **right**, and G(x) is stored in its bit-reversed form:

```
POLY = 0xEDB88320   (reflected form of direct 0x04C11DB7)
```

## Custom convention vs. standard zlib

This implementation differs from the standard zlib / ISO-HDLC CRC-32 in two ways:

| Property | This implementation | Standard zlib |
|---|---|---|
| Data enters register | Bit 31 (MSB side) | Bit 0 (LSB side), XOR'd directly |
| Feedback from | Bit 0 (LSB) | Bit 0 (LSB) |
| Finalization | 32-bit flush then XOR 0xFFFFFFFF | XOR 0xFFFFFFFF only |
| "123456789" | **0x22896B0A** | 0xCBF43926 |

Because data enters at the top instead of the bottom, one byte step becomes:

```
T(state, byte) = T(state, 0)  XOR  T(0, byte)
               = (state >> 8) XOR Ts[state & 0xFF]  XOR  Tb[byte]
```

Two separate tables `Ts` and `Tb` are needed (unlike zlib's single table where
data and feedback meet at the same end and can be merged).

## The linearity trick — basis for all table optimisations

The byte-step operator `T` is **linear over GF(2)**:

```
T(a XOR b, ...) = T(a, ...) XOR T(b, ...)
```

This makes it possible to precompute the effect of any block of bytes into tables
and XOR the results together — all lookups within a block are independent of each other,
which the CPU can execute in parallel (ILP).

## Slice-by-16 (n16)

Instead of one byte per loop iteration, n16 processes **16 bytes** at once.
For a block `b[0..15]` from state `s`:

```
state_new = M^16(s)  XOR  Σ_{j=0..15}  M^(15-j)( Tb[b[j]] )
```

where `M(x) = (x >> 8) XOR Ts[x & 0xFF]` is the state-propagation operator
(one byte step, no input data).

Precomputed tables:
- `Slice16[j][b]` = M^(15-j)(Tb[b]) — 16 tables, 256 entries each, 4 B per entry = 16 KB.
- `StateMix16[k][sb]` = M^16(sb << 8k) — 4 tables, decomposes M^16(s) by state byte = 4 KB.

Total: 20 KB — fits in 32 KB L1 cache. 20 independent lookups per 16 bytes.

## Parallel computation with GF(2) matrix stitching

For a data array split into chunks `chunk_0, chunk_1, … chunk_{n-1}`:

1. Each chunk is computed independently **starting from state 0**:
   ```
   partial_i = F(0, chunk_i)
   ```
2. Sequential *stitching* (cheap):
   ```
   s = INIT
   for each i:
       s = M^{L_i}(s)  XOR  partial_i
   return Finish(s)
   ```

The `M^{L_i}` operation — raising the state-propagation matrix to a large power —
would normally cost O(L) passes. Instead it is done with GF(2) **binary matrix
exponentiation** in O(32 log L): represent M as a 32×32 binary matrix (one uint per column),
then use repeated squaring.

Correctness: this produces exactly the same bit sequence of states as a single
sequential pass, just reordered by linearity.

## Finalization (32-bit flush)

After all data bytes, 32 zero bits are pushed through the LFSR:

```csharp
for (int k = 0; k < 32; k++) {
    bool bit = (fcs & 1) != 0;
    fcs = (fcs >> 1) & 0x7FFFFFFF;
    if (bit) fcs ^= POLY;
}
return fcs ^ 0xFFFFFFFF;
```

This is equivalent to appending a 32-bit zero word to the message before division,
and then inverting all output bits.
