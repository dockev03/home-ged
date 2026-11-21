<script setup lang="ts">
import { darkTheme, dateFrFR, frFR, NConfigProvider, NGlobalStyle, NMessageProvider, useOsTheme } from 'naive-ui'
import { computed } from 'vue'

// Le thème suit le système. Le choix mémorisé (PWA-03) arrive au sous-lot 1.4.
const osTheme = useOsTheme()
const theme = computed(() => (osTheme.value === 'dark' ? darkTheme : null))
</script>

<template>
  <NConfigProvider :theme="theme" :locale="frFR" :date-locale="dateFrFR">
    <NGlobalStyle />
    <NMessageProvider>
      <RouterView />
    </NMessageProvider>
  </NConfigProvider>
</template>
