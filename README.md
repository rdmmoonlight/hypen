Setuju. Langkah mundur ini sangat tepat agar arsitektur data kita benar-benar matang secara konsep sebelum menyentuh baris kode lagi. Kita butuh **Blueprint Pipeline (Jalur Pipa)** yang jelas: mulai dari file mentah/ekstraksi masuk, melewati gerbang-gerbang (*gates*) apa saja, mendapat stempel apa, hingga akhirnya sah masuk ke tabel Songs (Production Library).
Mari kita rancang **Pipeline Sertifikasi & Anti-Duplicate** dari hulu ke hilir:
### Alur Perjalanan Data (Pipeline Workflow)
```text
[ External Sources / YouTube / Local MP3 ]
                 │
                 ▼
          📦 [ RAW STAGING ]  <--- (Titik Masuk Utama)
                 │
                 ▼
       🚪 [ GATE 1: SANITY CHECK ]
       ├── Validasi Format (Title & Artist tidak boleh kosong/null)
       ├── Normalisasi Karakter (Pembersihan tanda baca/spasi ganda)
       └── 🏷️ Stempel: [RAW_VALIDATED] atau ditolak
                 │
                 ▼
       🚪 [ GATE 2: CROSS-LIBRARY CHECK (COMPLETE TABLE) ]
       ├── Bandingkan kombinasi (Normalized Artist + Title) langsung ke tabel `Songs` (Production)
       ├── Jika sudah ada di Library Utama: 
       │   └── 🏷️ Stempel: [ALREADY_IN_LIBRARY] ➔ Lempar/Tahan di Staging untuk dibuang.
       └── Jika belum ada:
           └── 🏷️ Stempel: [LIBRARY_SAFE]
                 │
                 ▼
       🚪 [ GATE 3: INTERNAL STAGING CONFLICT CHECK (DUPLICATE ARENA) ]
       ├── Scan kesamaan antar data yang ada di dalam Staging sendiri (misal: ekstraksi batch ganda)
       ├── Jika ditemukan kelompok mirip:
       │   ├── 🏷️ Stempel: [CONFLICT_DUPLICATE] ➔ Masuk Panel Arena Pembanding Interaktif.
       │   └── Harus melalui "Pertandingan": User/Admin memilih 1 sebagai **MASTER (KEEP)**, sisanya berstatus **DUPLICATE (PURGE)**.
       └── Jika tidak ada kemiripan internal:
           └── 🏷️ Stempel: [STAGING_UNIQUE]
                 │
                 ▼
       🎖️ [ FINAL GATE: CERTIFIED UNIQUE ]
       ├── Syarat Lolos: Harus berstatus `LIBRARY_SAFE` + `STAGING_UNIQUE` (atau sudah dipilih sebagai Master di Arena).
       └── 🏷️ Stempel Akhir: [CERTIFIED_READY]
                 │
                 ▼
       🚀 [ COMMIT / PROMOTION ] ➔ Masuk ke Tabel `Songs` (Production Library)

```
### Rincian 4 Gerbang Utama (Gates) & Aturannya
#### 1. Gate 1: Sanity & Format Check (Gerbang Kebersihan Dasar)
 * **Tujuan**: Menyaring data sampah yang tidak layak diproses.
 * **Kondisi Lolos**:
   * Title dan Artist wajib terisi (tidak boleh kosong atau string kosong).
 * **Jika Gagal**: Data tetap di Staging dengan label error, tombol *Commit* dikunci total sampai diperbaiki manual oleh user di form tabel.
#### 2. Gate 2: Production Library Check (Gerbang Benteng Utama)
 * **Tujuan**: Mencegah lagu yang *sudah ada* di tabel Songs masuk kembali.
 * **Mekanisme**: Sistem mencocokkan string Normalized(Artist) + Normalized(Title) terhadap database *Complete Library*.
 * **Jika Gagal**:
   * Diberi label khusus (misal: "Sudah ada di Library").
   * **Aksi**: Tombol *Commit* mati. Sistem menyarankan user untuk langsung menghapus (*Delete*) data tersebut dari Staging agar tidak memenuhi buffer.
#### 3. Gate 3: Internal Staging Conflict Arena (Gerbang Pertandingan Duplikat)
 * **Tujuan**: Menangani duplikasi internal yang masuk secara bersamaan (misal mengunduh file atau lagu yang sama dua kali di sesi ekstraksi berbeda).
 * **Mekanisme**:
   * Dijalankan oleh *Engine* pembanding (mirip *Duplicate Finder*).
   * Data dikelompokkan ke dalam *Duplicate Groups* (berdasarkan kemiripan judul, artis, atau durasi).
 * **Aturan Main di Arena**:
   * User wajib memilih salah satu sebagai **Master (KEEP)**.
   * Item lain dalam kelompok yang sama otomatis berstatus **Kalah / Purge Candidate**.
   * Selama sebuah item masih berstatus konflik dan belum ditentukan masternya, ia **haram hukumnya** untuk di-commit. Tombol *Commit* untuk item tersebut terkunci.
#### 4. Final Gate: Sertifikasi & Commit (Gerbang Pengesahan)
 * **Tujuan**: Memastikan hanya data yang 100% bersih yang menjejakkan kaki di tabel Songs.
 * **Kondisi Lolos Mutlak**:
   1. Lolos Gate 1 (Format valid).
   2. Lolos Gate 2 (Tidak ada di Library utama).
   3. Lolos Gate 3 (Tidak berkonflik, atau sudah sah menjadi Master yang dipilih di Duplicate Arena).
 * **Stempel Akhir**: [CERTIFIED_UNIQUE].
 * **Aksi**: Tombol Commit Certified Safe atau *Commit Single* menyala hijau, mengizinkan data dipromosikan ke tabel Songs.
Bagaimana menurutmu rancangan alur gerbang (*gates*) dan stempel sertifikasi di atas? Apakah sudah sesuai dengan standar disiplin data yang kamu inginkan untuk database lagu ini?
