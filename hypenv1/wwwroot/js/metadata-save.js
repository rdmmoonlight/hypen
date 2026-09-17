// Save metadata langsung via fetch() ke /api/metadata/save.
// Sengaja TIDAK lewat Blazor (@onclick) supaya tidak bergantung
// pada circuit SignalR yang bisa putus kalau tab idle / Render sleep.
// Semua nilai dibaca langsung dari DOM (id attribute), bukan dari state C#.

function hypenShowStatus(message, isError) {
    var box = document.getElementById('js-save-status');
    if (!box) return;
    box.style.display = 'block';
    box.textContent = message;
    box.className = 'alert py-1 px-2 small mb-2 shadow-sm rounded-2 ' +
        (isError ? 'alert-danger' : 'alert-success');
}

function hypenGetVal(id) {
    var el = document.getElementById(id);
    return el ? el.value : '';
}

async function hypenSaveMetadata() {
    var btn = document.getElementById('js-save-btn');
    var idRaw = hypenGetVal('active-item-id');

    if (!idRaw) {
        hypenShowStatus('Tidak ada lagu yang sedang dipilih.', true);
        return;
    }

    var releaseYearRaw = hypenGetVal('year-input');
    var durationRaw = hypenGetVal('duration-input');

    var payload = {
        id: parseInt(idRaw, 10),
        isFromRawSongs: hypenGetVal('active-item-israw') === 'true',
        filePath: hypenGetVal('active-item-filepath'),
        fileName: hypenGetVal('active-item-filename'),
        title: hypenGetVal('title-input'),
        artist: hypenGetVal('artist-input'),
        album: hypenGetVal('album-input'),
        releaseYear: releaseYearRaw ? parseInt(releaseYearRaw, 10) : null,
        albumCoverUrl: hypenGetVal('cover-input'),
        durationSeconds: durationRaw ? parseInt(durationRaw, 10) : 0,
        musicBrainzId: hypenGetVal('mbid-input'),
        country: hypenGetVal('country-input')
    };

    if (btn) { btn.disabled = true; }
    hypenShowStatus('Menyimpan...', false);

    try {
        var response = await fetch('/api/metadata/save', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            var errText = await response.text();
            hypenShowStatus('Gagal menyimpan (HTTP ' + response.status + '): ' + errText, true);
            if (btn) { btn.disabled = false; }
            return;
        }

        var result = await response.json();
        hypenShowStatus('Berhasil disimpan ke tabel ' + result.table + '.', false);

        // Reload penuh supaya grid & circuit Blazor kembali segar.
        setTimeout(function () { window.location.reload(); }, 800);
    } catch (err) {
        hypenShowStatus('Gagal menyimpan: ' + err.message, true);
        if (btn) { btn.disabled = false; }
    }
}

window.hypenSaveMetadata = hypenSaveMetadata;
