<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute } from 'vue-router';
import { CheckCircleFilledIcon, ErrorCircleFilledIcon, LoadingIcon } from 'tdesign-icons-vue-next';
import { MessagePlugin } from 'tdesign-vue-next';
import { completeLoliaAuthorize } from '@/pages/frp/createFrp/components/LoliaFrp/auth';

const route = useRoute();
const status = ref<'loading' | 'success' | 'error'>('loading');
const message = ref('正在完成 Lolia FRP 授权...');

onMounted(async () => {
  const code = route.query.code as string | undefined;
  const state = route.query.state as string | undefined;
  const oauthError = route.query.error_description as string | undefined;

  if (oauthError) {
    status.value = 'error';
    message.value = oauthError;
    return;
  }

  if (!code || !state) {
    status.value = 'error';
    message.value = 'Lolia 回调缺少 Code 或 State。';
    return;
  }

  try {
    const profile = await completeLoliaAuthorize(code, state);
    status.value = 'success';
    message.value = `${profile?.username || '账号'} 已成功连接，窗口即将关闭`;
    window.localStorage.setItem('lolia_oauth_success', Date.now().toString());
    window.setTimeout(() => window.close(), 1000);
  } catch (error: any) {
    status.value = 'error';
    message.value = error?.response?.data?.msg || error?.message || 'Lolia 授权失败';
    MessagePlugin.error(message.value);
  }
});
</script>

<template>
  <div class="min-h-screen flex items-center justify-center p-6 bg-zinc-50 dark:bg-zinc-900">
    <div class="w-full max-w-md p-8 text-center rounded-3xl border border-zinc-200/70 dark:border-zinc-700/60 bg-white dark:bg-zinc-800 shadow-xl">
      <loading-icon v-if="status === 'loading'" size="48px" class="animate-spin text-[var(--color-primary)]" />
      <check-circle-filled-icon v-else-if="status === 'success'" size="52px" class="text-emerald-500" />
      <error-circle-filled-icon v-else size="52px" class="text-red-500" />
      <h2 class="mt-5 mb-3 text-xl font-bold">
        {{ status === 'success' ? '连接成功' : status === 'error' ? '授权失败' : '正在连接' }}
      </h2>
      <p class="text-sm text-[var(--td-text-color-secondary)] break-all">{{ message }}</p>
    </div>
  </div>
</template>
