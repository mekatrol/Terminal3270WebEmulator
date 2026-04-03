<template>
  <main class="unauthorized-page">
    <section class="unauthorized-panel" aria-labelledby="unauthorized-title">
      <h1 id="unauthorized-title">Access denied</h1>
      <p>
        Your Entra ID account is not authenticated for the terminal emulator or does not hold a
        required role claim.
      </p>
      <p>
        This route is intended to remain accessible without authentication, as described in the
        project design rules.
      </p>
    </section>
  </main>
</template>

<style scoped>
.unauthorized-page {
  display: grid;
  min-height: 100vh;
  place-items: center;
  padding: 2rem;
  background: linear-gradient(180deg, #f4f8f8 0%, #dde7e8 100%);
  color: #183136;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.unauthorized-panel {
  max-width: 42rem;
  border: 1px solid rgb(24 49 54 / 20%);
  padding: 2rem;
  background: rgb(255 255 255 / 90%);
  box-shadow: 0 1rem 2.5rem rgb(24 49 54 / 10%);
}
</style>
