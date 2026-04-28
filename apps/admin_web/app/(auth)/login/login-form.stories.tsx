import type { Meta, StoryObj } from "@storybook/react";
import { LoginForm } from "./login-form";

const meta: Meta<typeof LoginForm> = {
  component: LoginForm,
  title: "auth/LoginForm",
  parameters: {
    nextjs: { router: { basePath: "" } },
  },
};
export default meta;
type Story = StoryObj<typeof LoginForm>;

export const Default: Story = {};
