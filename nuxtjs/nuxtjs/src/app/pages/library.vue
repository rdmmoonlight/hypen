<template>
  <div class="min-h-screen bg-[#0a0a0a] text-zinc-100">
    <div class="max-w- mx-auto p-6 md:p-8">
      <div class="flex justify-between items-center mb-8">
        <div>
          <div class="text- tracking-[0.3em] text-zinc-500 font-mono uppercase mb-2">Stage 03 // Final</div>
          <h1 class="text-4xl font-black tracking-tight">Music Library</h1>
          <p class="text-zinc-500 font-mono text-sm mt-2">{{ songs?.length || 0 }} tracks di DB</p>
        </div>
        <NuxtLink to="/home" class="h-10 px-5 rounded-full bg-zinc-900 border border-zinc-800 text-sm font-mono flex items-center hover:border-zinc-600 transition">← CONTROL CENTER</NuxtLink>
      </div>

      <!-- Search -->
      <div class="rounded- bg-zinc-900 border border-zinc-800 p-4 mb-6 flex gap-3">
        <input v-model="q" placeholder="Cari title / artist..." class="flex-1 bg-zinc-950 border border-zinc-800 rounded-xl px-4 py-3 text-sm outline-none focus:border-zinc-600" />
        <div class="px-4 py-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 font-mono text-sm">{{ filtered.length }} found</div>
      </div>

      <!-- Table -->
      <div class="rounded- bg-zinc-900 border border-zinc-800 overflow-hidden">
        <div class="overflow-x-auto">
          <table class="w-full text-left text-sm">
            <thead class="bg-zinc-950 text- font-mono uppercase text-zinc-500">
              <tr>
                <th class="px-6 py-3 w-12">#</th>
                <th class="px-4 py-3">Title</th>
                <th class="px-4 py-3">Artist</th>
                <th class="px-4 py-3">Created</th>
              </tr>
            </thead>
            <tbody class="divide-y divide-zinc-800/60">
              <tr v-for="s in filtered" :key="s.id" class="hover:bg-zinc-800/40 transition">
                <td class="px-6 py-3 font-mono text-xs text-zinc-500">{{ s.id }}</td>
                <td class="px-4 py-3 font-medium text-white">{{ s.title }}</td>
                <td class="px-4 py-3 text-zinc-300">{{ s.artist || '—' }}</td>
                <td class="px-4 py-3 font-mono text-xs text-zinc-500">{{ new Date(s.createdAt).toLocaleDateString('id-ID') }}</td>
              </tr>
              <tr v-if="filtered.length===0">
                <td colspan="4" class="px-6 py-12 text-center font-mono text-zinc-500">Belum ada lagu di library. Commit dulu dari Staging.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
useHead({ title: 'Library - Hypen Vault' })
const q = ref('')
const { data: songs } = await useFetch('/api/library', { query: { search: q }, watch: [q] })

const filtered = computed(() => {
  if (!songs.value) return []
  const list = songs.value as any[]
  if (!q.value) return list
  const s = q.value.toLowerCase()
  return list.filter((x: any) => x.title.toLowerCase().includes(s) || (x.artist||'').toLowerCase().includes(s))
})
</script>
