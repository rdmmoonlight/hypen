<!-- pages/index.vue -->
<script setup lang="ts">
const handleEnter = () => {
  navigateTo('/home')
}
</script>

<template>
  <div>
    <!-- Nuxt Welcome Screen Bawaan (Tetap utuh) -->
    <NuxtWelcome />

    <!-- Teleport memaksa tombol keluar dari konteks NuxtWelcome dan ditaruh langsung di <body> -->
    <ClientOnly>
      <Teleport to="body">
        <div class="fixed top-6 right-6 z-[999999] pointer-events-auto">
          <button 
            type="button"
            class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold px-6 py-3 rounded-lg shadow-2xl transition-all hover:scale-105 flex items-center gap-2 cursor-pointer border-0"
            @click="handleEnter"
          >
            <span>Masuk ke Dalam Web</span>
            <Icon name="tabler:arrow-right" class="w-5 h-5" />
          </button>
        </div>
      </Teleport>
    </ClientOnly>
  </div>
</template>
