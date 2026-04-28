import type { Meta, StoryObj } from "@storybook/react";
import { ForbiddenState } from "./forbidden-state";

const meta: Meta<typeof ForbiddenState> = {
  component: ForbiddenState,
  title: "shell/ForbiddenState",
};
export default meta;
type Story = StoryObj<typeof ForbiddenState>;

export const Default: Story = {};
