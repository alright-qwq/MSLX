<script setup lang="ts">
import { ref, reactive, computed, watch } from 'vue';
import { MessagePlugin } from 'tdesign-vue-next';
import { generateRandomString } from '@/utils/tools';
import { createLoliaTunnel, type LoliaNode } from '../auth';

const props = defineProps<{
  visible: boolean;
  nodes: LoliaNode[];
}>();

const emit = defineEmits(['update:visible', 'success']);

const submitting = ref(false);

const form = reactive({
  nodeId: undefined as number | undefined,
  type: 'TCP',
  tunnelname: '',
  localip: '127.0.0.1',
  localport: '25565',
  remoteport: '',
  customDomain: '',
});

const selectedNode = computed(() => props.nodes.find((node) => node.id === form.nodeId) || null);

const groupedNodes = computed(() => {
  const groupsMap = new Map<string, { label: string; value: string; children: LoliaNode[] }>();

  props.nodes.forEach((node) => {
    const groupLabel = node.region_code || '节点';

    if (!groupsMap.has(groupLabel)) {
      groupsMap.set(groupLabel, { label: groupLabel, value: groupLabel, children: [] });
    }
    groupsMap.get(groupLabel)!.children.push(node);
  });

  return Array.from(groupsMap.values());
});

const generateRandomData = () => {
  form.tunnelname = 'MSLX_' + generateRandomString(6);
  form.remoteport = (Math.floor(Math.random() * (65535 - 10000 + 1)) + 10000).toString();
};

watch(
  () => props.visible,
  (val) => {
    if (val && props.nodes.length > 0 && !form.nodeId) {
      form.nodeId = props.nodes[0].id;
      generateRandomData();
    }
  },
);

const handleConfirm = async () => {
  if (!form.nodeId) {
    MessagePlugin.warning('请选择一个节点');
    return;
  }
  if (!form.tunnelname || !form.localip || !form.localport || !form.remoteport) {
    MessagePlugin.warning('请填写完整的映射配置');
    return;
  }

  submitting.value = true;
  try {
    await createLoliaTunnel({
      node_id: form.nodeId,
      type: form.type.toLowerCase(),
      local_ip: form.localip,
      local_port: parseInt(form.localport),
      remote_port: parseInt(form.remoteport),
      custom_domain: ['http', 'https'].includes(form.type.toLowerCase()) ? form.customDomain.trim() : '',
      remark: form.tunnelname,
    });

    MessagePlugin.success(`隧道 ${form.tunnelname} 创建成功！`);
    emit('success');
    emit('update:visible', false);
  } catch (e: any) {
    const errorMsg = e.message || e.response?.data?.msg || e.msg || '请检查配置或节点状态';
    MessagePlugin.error(`创建失败: ${errorMsg}`);
  } finally {
    submitting.value = false;
  }
};
</script>

<template>
  <t-dialog
    attach="body"
    :visible="visible"
    header="新建 Lolia FRP 隧道"
    width="580px"
    :confirm-btn="{ content: '提交创建', loading: submitting }"
    @confirm="handleConfirm"
    @close="emit('update:visible', false)"
  >
    <t-form
      :data="form"
      label-align="right"
      :label-width="100"
      class="pt-2.5 overflow-x-hidden [&_.t-form__item]:!mb-[22px]"
    >
      <t-form-item label="选择节点" name="nodeId">
        <t-select
          v-model="form.nodeId"
          filterable
          placeholder="请选择节点"
          :popup-props="{ overlayClassName: 'max-h-[300px]' }"
          @change="generateRandomData"
        >
          <t-option-group v-for="group in groupedNodes" :key="group.value" :label="group.label">
            <t-option v-for="node in group.children" :key="node.id" :value="node.id" :label="node.name">
              <div class="flex justify-between items-center w-full">
                <span class="truncate">{{ node.name }}</span>
                <span class="text-xs text-zinc-400 shrink-0 ml-2">{{ node.region_code }}</span>
              </div>
            </t-option>
          </t-option-group>
        </t-select>
      </t-form-item>

      <t-form-item v-if="selectedNode" label="节点详情">
        <div class="w-full flex flex-col gap-2.5">
          <div
            class="bg-[var(--td-bg-color-secondarycontainer)] rounded-[var(--td-radius-medium)] p-3 border border-dashed border-[var(--td-component-border)]"
          >
            <pre
              class="m-0 whitespace-pre-wrap break-all text-[13px] text-[var(--td-text-color-primary)] leading-[1.6]"
              >{{ selectedNode.remark || '此节点暂无备注' }}</pre
            >
          </div>
        </div>
      </t-form-item>

      <t-form-item label="隧道类型">
        <t-select v-model="form.type">
          <t-option label="TCP" value="TCP" />
          <t-option label="UDP" value="UDP" />
          <t-option label="HTTP" value="HTTP" />
          <t-option label="HTTPS" value="HTTPS" />
        </t-select>
      </t-form-item>

      <t-row :gutter="[16, 20]">
        <t-col :xs="12" :sm="6">
          <t-form-item label="隧道名称">
            <t-input v-model="form.tunnelname" />
          </t-form-item>
        </t-col>
        <t-col :xs="12" :sm="6">
          <t-form-item label="远程端口">
            <t-input v-model="form.remoteport" placeholder="留空由服务端分配">
              <template #suffix>
                <t-button variant="text" size="small" @click="generateRandomData">随机</t-button>
              </template>
            </t-input>
          </t-form-item>
        </t-col>
        <t-col :xs="12" :sm="6">
          <t-form-item label="本地IP">
            <t-input v-model="form.localip" />
          </t-form-item>
        </t-col>
        <t-col :xs="12" :sm="6">
          <t-form-item label="本地端口">
            <t-input v-model="form.localport" />
          </t-form-item>
        </t-col>
        <t-col v-if="['http', 'https'].includes(form.type.toLowerCase())" :xs="12">
          <t-form-item label="绑定域名">
            <t-input v-model="form.customDomain" placeholder="example.lolia.link" />
          </t-form-item>
        </t-col>
      </t-row>
    </t-form>
  </t-dialog>
</template>

<style scoped></style>
