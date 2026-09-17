<!-- pages/home.vue - Hypen Music Vault Pipeline Hub -->
<template>
  <div class="min-h-screen bg-zinc-950 text-white relative overflow-hidden">
    <!-- subtle grid background -->
    <div class="absolute inset-0 bg-[linear-gradient(to_right,#222_1px,transparent_1px),linear-gradient(to_bottom,#222_1px,transparent_1px)] bg-[size:40px_40px] opacity-[0.15] pointer-events-none" />

    <div class="relative max-w- mx-auto px-6 py-10 md:px-10 md:py-16 flex flex-col items-center">

      <!-- TOP SUCCESS BADGE -->
      <div class="inline-flex items-center gap-2 bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 px-4 py-1.5 rounded-full text-xs font-mono mb-8">
        <Icon name="tabler:circle-check" class="w-4 h-4" />
        Berhasil Masuk ke Halaman Web!
      </div>

      <div class="text-center mb-12">
        <div class="text- tracking-[0.4em] text-zinc-500 font-mono uppercase mb-3">Hypen Music Vault // System Pipeline</div>
        <h1 class="text-5xl md:text-7xl font-black tracking-tighter">CONTROL CENTER</h1>
        <p class="text-zinc-400 font-mono text-sm mt-4 max-w- mx-auto">Pilih pipeline stage. Semua flow Extraction -> Staging -> Library sudah terhubung.</p>
      </div>

      <!-- PIPELINE STATS -->
      <div class="flex items-center gap-2 font-mono text- mb-10 bg-zinc-900 border border-zinc-800 rounded-full p-1.5">
        <div class="px-4 py-1">RAW: <span class="text-white font-bold">{{ pendingRawCount }}</span></div>
        <div class="w-px h-4 bg-zinc-800" />
        <div class="px-4 py-1">STAGED: <span class="text-amber-400 font-bold">{{ pendingRawCount }}</span></div>
        <div class="w-px h-4 bg-zinc-800" />
        <div class="px-4 py-1">LIBRARY: <span class="text-emerald-400 font-bold">{{ completedSongsCount.toLocaleString() }}</span></div>
        <div class="w-2 h-2 rounded-full bg-emerald-500 animate-pulse ml-2 mr-2" />
      </div>

      <!-- 3 CARDS NAVIGATION -->
      <div class="grid md:grid-cols-3 gap-6 w-full max-w-">

        <!-- 1. EXTRACTION -->
        <NuxtLink to="/extraction" class="group rounded- bg-zinc-900 border border-zinc-800 p-7 hover:border-zinc-600 hover:bg-zinc-900 transition-all duration-300 flex flex-col text-left">
          <div class="flex justify-between items-start mb-6">
            <div class="w-12 h-12 rounded-full bg-white text-black flex items-center justify-center">
              <Icon name="tabler:cloud-download" class="w-6 h-6" />
            </div>
            <Icon name="tabler:arrow-up-right" class="w-5 h-5 text-zinc-600 group-hover:text-white group-hover:rotate-45 transition-all duration-300" />
          </div>
          <div class="text- font-mono tracking-widest text-zinc-500 uppercase mb-2">Stage 01 // Ingest</div>
          <h3 class="text-2xl font-bold mb-2">Extraction Engine</h3>
          <p class="text-sm text-zinc-400 leading-relaxed mb-8">Tarik dari YouTube playlist atau scan folder lokal. Parsing & duplicate check sebelum masuk staging.</p>
          <div class="mt-auto">
            <span class="inline-flex items-center gap-2 bg-white text-black font-mono font-bold text-xs px-5 py-3 rounded-full group-hover:bg-zinc-200 transition">
              OPEN ENGINE <Icon name="tabler:arrow-right" class="w-4 h-4" />
            </span>
          </div>
        </NuxtLink>

        <!-- 2. STAGING -->
        <NuxtLink to="/staging" class="group rounded- bg-[#151515] border border-amber-900/30 p-7 hover:border-amber-700/50 transition-all duration-300 flex flex-col text-left relative overflow-hidden">
          <div class="absolute -right-10 -top-10 w-40 h-40 bg-amber-500/10 rounded-full blur-2xl group-hover:bg-amber-500/20 transition" />
          <div class="flex justify-between items-start mb-6 relative">
            <div class="w-12 h-12 rounded-full bg-amber-500/15 text-amber-400 border border-amber-500/20 flex items-center justify-center">
              <Icon name="tabler:layers-intersect" class="w-6 h-6" />
            </div>
            <span class="text- font-mono bg-amber-500 text-black font-bold px-3 py-1 rounded-full">{{ pendingRawCount }} PENDING</span>
          </div>
          <div class="text- font-mono tracking-widest text-amber-500/70 uppercase mb-2">Stage 02 // Buffer</div>
          <h3 class="text-2xl font-bold mb-2">Staging Buffer</h3>
          <p class="text-sm text-zinc-400 leading-relaxed mb-8">Review, clean duplicate & commit ke library permanen. {{ pendingRawCount }} lagu nunggu aksi.</p>
          <div class="mt-auto relative">
            <span class="inline-flex items-center gap-2 bg-amber-500 text-black font-mono font-bold text-xs px-5 py-3 rounded-full group-hover:bg-amber-400 transition">
              OPEN STAGING ({{ pendingRawCount }}) <Icon name="tabler:arrow-right" class="w-4 h-4" />
            </span>
          </div>
        </NuxtLink>

        <!-- 3. LIBRARY -->
        <NuxtLink to="/library" class="group rounded- bg-zinc-900 border border-zinc-800 p-7 hover:border-emerald-800/50 hover:bg-zinc-900 transition-all duration-300 flex flex-col text-left relative overflow-hidden">
          <div class="absolute -right-10 -top-10 w-40 h-40 bg-emerald-500/10 rounded-full blur-2xl group-hover:bg-emerald-500/20 transition" />
          <div class="flex justify-between items-start mb-6 relative">
            <div class="w-12 h-12 rounded-full bg-emerald-500/15 text-emerald-400 border border-emerald-500/20 flex items-center justify-center">
              <Icon name="tabler:library" class="w-6 h-6" />
            </div>
            <Icon name="tabler:arrow-up-right" class="w-5 h-5 text-zinc-600 group-hover:text-white group-hover:rotate-45 transition-all duration-300" />
          </div>
          <div class="text- font-mono tracking-widest text-zinc-500 uppercase mb-2">Stage 03 // Final</div>
          <h3 class="text-2xl font-bold mb-2">Music Library</h3>
          <p class="text-sm text-zinc-400 leading-relaxed mb-8">Koleksi final yang sudah clean. {{ completedSongsCount.toLocaleString() }} tracks siap streaming & management.</p>
          <div class="mt-auto relative">
            <span class="inline-flex items-center gap-2 bg-zinc-800 text-white border border-zinc-700 font-mono font-bold text-xs px-5 py-3 rounded-full group-hover:bg-zinc-700 transition">
              ← OPEN LIBRARY ({{ completedSongsCount.toLocaleString() }})
            </span>
          </div>
        </NuxtLink>
      </div>

      <!-- BOTTOM ACTION -->
      <NuxtLink to="/" class="mt-12 opacity-60 hover:opacity-100 transition">
        <Button variant="outline" class="gap-2 rounded-full font-mono text-xs">
          <Icon name="tabler:arrow-left" class="w-4 h-4" />
          Kembali ke Landing Page
        </Button>
      </NuxtLink>
    </div>
  </div>
</template>

<script setup lang="ts">
useHead({ title: 'Hypen Music Vault - Control Center' })

const pendingRawCount = ref(128)
const completedSongsCount = ref(2450)
</script>
