<!-- pages/index.vue -->
<script setup lang="ts">
import { Button } from '@/components/ui/button'

const handleEnter = () => {
  navigateTo('/home')
}
</script>

<template>
  <!-- Wrapper utama dibuat relatif dengan tinggi penuh viewport -->
  <div class="relative w-full h-screen overflow-hidden">
    
    <!-- 1. Floating Action Button ditaruh PADA CONTAINER TERPISAH ber-z-index sangat tinggi -->
    <div class="fixed top-6 right-6 z-[99999] pointer-events-auto">
      <Button 
        size="lg" 
        class="bg-emerald-500 hover:bg-emerald-600 text-black font-semibold px-6 shadow-2xl transition-all hover:scale-105 gap-2 cursor-pointer relative"
        @click="handleEnter"
      >
        <span>Masuk ke Dalam Web</span>
        <ClientOnly>
          <Icon name="tabler:arrow-right" class="w-5 h-5" />
        </ClientOnly>
      </Button>
    </div>

    <!-- 2. NuxtWelcome ditaruh di dalam pembungkus ber-z-index rendah -->
    <div class="relative z-0 h-full overflow-y-auto">
      <NuxtWelcome />
    </div>

  </div>
</template>
