const form = document.querySelector('#upload');
const input = document.querySelector('#image');
const button = form.querySelector('button');
const status = document.querySelector('#status');
const active = document.querySelector('#active');
const empty = document.querySelector('#empty');
const recent = document.querySelector('#recent');
const maxImageBytes = 50_000_000;
const imageTooLarge = 'התמונה גדולה מ־50 MB.';
let busy = false;

function imageUrl(name) {
  return '/images/' + encodeURIComponent(name);
}

function setBusy(value) {
  busy = value;
  button.disabled = value;
  recent.setAttribute('aria-busy', String(value));
}

async function refresh() {
  const response = await fetch('/api/images', { cache: 'no-store' });
  if (!response.ok) throw new Error('Could not load images');

  const { activeImage, recentImages } = await response.json();
  active.hidden = !activeImage;
  empty.hidden = Boolean(activeImage);
  if (activeImage) active.src = imageUrl(activeImage);
  recent.replaceChildren(...recentImages.map((name, index) => {
    const item = document.createElement('li');
    const choice = document.createElement('button');
    choice.type = 'button';
    choice.dataset.image = name;
    choice.setAttribute('aria-label', 'הצגת תמונה ' + (index + 1));
    choice.setAttribute('aria-pressed', String(name === activeImage));
    const image = document.createElement('img');
    image.src = imageUrl(name);
    image.alt = '';
    choice.append(image);
    item.append(choice);
    return item;
  }));
}

recent.addEventListener('click', async event => {
  const choice = event.target.closest('button');
  if (!choice || busy) return;

  const name = choice.dataset.image;
  setBusy(true);
  try {
    const response = await fetch('/api/images/' + encodeURIComponent(name) + '/select', {
      method: 'POST',
      headers: { 'X-Picture-Monitor': 'select' }
    });
    if (!response.ok) throw new Error('Could not select image');
    await refresh();
    [...recent.querySelectorAll('button')].find(button => button.dataset.image === name)?.focus();
    status.textContent = 'התמונה נבחרה לתצוגה.';
  } catch {
    status.textContent = 'לא ניתן לעדכן את הבחירה בדף. אפשר לרענן ולנסות שוב.';
  } finally {
    setBusy(false);
  }
});

form.addEventListener('submit', async event => {
  event.preventDefault();
  if (busy) return;
  const file = input.files[0];
  setBusy(true);
  status.textContent = 'מעלה תמונה…';
  try {
    if (file.size > maxImageBytes) {
      status.textContent = imageTooLarge;
      return;
    }
    try {
      const bitmap = await createImageBitmap(file);
      bitmap.close();
    } catch {
      status.textContent = 'לא ניתן לפתוח את קובץ התמונה.';
      return;
    }
    const response = await fetch('/upload', { method: 'POST', body: file });
    if (response.status === 413) {
      status.textContent = imageTooLarge;
      return;
    }
    if (!response.ok) {
      status.textContent = response.status === 415
        ? 'בחר תמונה בפורמט JPEG או PNG.'
        : 'העלאת התמונה נכשלה. אפשר לנסות שוב.';
      return;
    }
    input.value = '';
    status.textContent = 'התמונה הועלתה ונבחרה לתצוגה.';
    await refresh().catch(() => {});
  } catch {
    status.textContent = 'החיבור לשרת נקטע. בדוק איזו תמונה פעילה לפני ניסיון חוזר.';
  } finally {
    setBusy(false);
  }
});

refresh().catch(() => {
  status.textContent = 'אין כרגע חיבור לשרת.';
});
