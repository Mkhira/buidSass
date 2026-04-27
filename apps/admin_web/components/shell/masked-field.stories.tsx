/**
 * Storybook stories for <MaskedField>.
 *
 * Coverage matrix: kind ∈ {email, phone, generic} × canRead ∈ {true, false}.
 * The locale + theme dimensions are driven by the Storybook toolbar
 * (preview.tsx).
 */
import type { Meta, StoryObj } from "@storybook/react";
import { MaskedField } from "./masked-field";

const meta: Meta<typeof MaskedField> = {
  component: MaskedField,
  title: "shell/MaskedField",
  argTypes: {
    kind: { control: { type: "radio" }, options: ["email", "phone", "generic"] },
    canRead: { control: { type: "boolean" } },
  },
};
export default meta;
type Story = StoryObj<typeof MaskedField>;

export const EmailUnmasked: Story = {
  args: { kind: "email", value: "admin@dental-commerce.com", canRead: true },
};
export const EmailMasked: Story = {
  args: { kind: "email", value: "admin@dental-commerce.com", canRead: false },
};
export const PhoneUnmasked: Story = {
  args: { kind: "phone", value: "+966555000112", canRead: true },
};
export const PhoneMasked: Story = {
  args: { kind: "phone", value: "+966555000112", canRead: false },
};
export const GenericMasked: Story = {
  args: { kind: "generic", value: "secret-value", canRead: false },
};
