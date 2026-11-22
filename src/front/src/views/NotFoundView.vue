<template>
  <main class="introuvable">
    <p class="display-4 mb-0">404</p>
    <h1 class="h3">Page introuvable</h1>
    <p class="text-body-secondary">Cette adresse ne correspond à aucune page.</p>
    <RouterLink class="btn btn-primary" to="/">Retour à l'accueil</RouterLink>
  </main>
</template>

<style scoped>
.introuvable {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 3rem 1rem;
  text-align: center;
}
</style>
