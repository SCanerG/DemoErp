import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AiResponseText } from './AiResponseText';
describe('safe AI explanations', () => {
  it('renders Turkish strong text, paragraphs and lists without raw Markdown', () => {
    const { container } = render(<AiResponseText text={'### Stok değerlendirmesi\n\n**Sıcaklık Sensörü** için stok sıfır.\n\n- Yeniden tedarik edin.\n- _Kaynağı_ inceleyin.'} />);
    expect(screen.getByRole('heading', { name: 'Stok değerlendirmesi' })).toBeInTheDocument();
    expect(screen.getByText('Sıcaklık Sensörü').tagName).toBe('STRONG'); expect(screen.getAllByRole('listitem')).toHaveLength(2);
    expect(container.textContent).not.toContain('**'); expect(container.textContent).not.toContain('###');
    expect(screen.getByText('Kaynağı').tagName).toBe('EM'); expect(container.textContent).not.toContain('_Kaynağı_');
  });
  it('never executes model HTML or creates model-supplied links and images', () => {
    const { container } = render(<AiResponseText text={'<script>alert(1)</script> **Ürün**\n[Kaynak](javascript:alert) ![Görsel](https://attacker.invalid/a)'} />);
    expect(container.querySelector('script')).toBeNull(); expect(container.querySelector('img')).toBeNull(); expect(container.querySelector('a')).toBeNull();
    expect(container.textContent).toContain('<script>'); expect(container.textContent).toContain('Kaynak');
  });
  it('renders numbered lists and code fences as escaped text', () => {
    const { container } = render(<AiResponseText text={'1. First\n2. Second\n\n```text\n<b>raw</b>\n```'} />);
    expect(container.querySelector('ol')).not.toBeNull(); expect(container.querySelector('pre')?.textContent).toBe('<b>raw</b>'); expect(container.querySelector('b')).toBeNull();
  });
});
