// Progressive enhancement for media uploads: pick a file, position/zoom it inside
// the target aspect, APPLY posts the cropped image to the normal upload endpoint.
// Without JS the plain file input + submit keep working — this only hijacks forms
// marked data-media-crop. Cropping is wholly client-side; no server round-trips.
(function () {
    'use strict';

    function init(form) {
        if (form.dataset.cropInit) {
            return;
        }
        form.dataset.cropInit = '1';

        var fileInput = form.querySelector('input[type=file]');
        var ui = form.querySelector('.crop-ui');
        var canvas = form.querySelector('canvas');
        var zoom = form.querySelector('.crop-zoom');
        var applyBtn = form.querySelector('.crop-apply');
        var cancelBtn = form.querySelector('.crop-cancel');
        var plainSubmit = form.querySelector('.plain-submit');
        if (!fileInput || !ui || !canvas || !zoom || !applyBtn || !cancelBtn) {
            return;
        }

        var aspect = parseFloat(form.dataset.aspect) || 1;
        var previewWidth = 320;
        canvas.width = previewWidth;
        canvas.height = Math.round(previewWidth / aspect);
        var ctx = canvas.getContext('2d');

        var img = null;
        var scale = 1;
        var minScale = 1;
        var offsetX = 0;
        var offsetY = 0;
        var drag = null;

        // JS is alive: the crop path replaces the plain submit entirely.
        if (plainSubmit) {
            plainSubmit.hidden = true;
        }
        form.addEventListener('submit', function (e) { e.preventDefault(); });

        function clampOffsets() {
            offsetX = Math.min(0, Math.max(canvas.width - img.width * scale, offsetX));
            offsetY = Math.min(0, Math.max(canvas.height - img.height * scale, offsetY));
        }

        function draw() {
            ctx.fillStyle = '#111';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(img, offsetX, offsetY, img.width * scale, img.height * scale);
        }

        fileInput.addEventListener('change', function () {
            var file = fileInput.files && fileInput.files[0];
            if (!file) {
                return;
            }
            var url = URL.createObjectURL(file);
            var loaded = new Image();
            loaded.onload = function () {
                img = loaded;
                minScale = Math.max(canvas.width / img.width, canvas.height / img.height);
                scale = minScale;
                offsetX = (canvas.width - img.width * scale) / 2;
                offsetY = (canvas.height - img.height * scale) / 2;
                zoom.value = '0';
                ui.hidden = false;
                clampOffsets();
                draw();
                URL.revokeObjectURL(url);
            };
            loaded.src = url;
        });

        zoom.addEventListener('input', function () {
            if (!img) {
                return;
            }
            var centerX = canvas.width / 2;
            var centerY = canvas.height / 2;
            var previous = scale;
            scale = minScale * (1 + parseFloat(zoom.value));
            offsetX = centerX - (centerX - offsetX) * (scale / previous);
            offsetY = centerY - (centerY - offsetY) * (scale / previous);
            clampOffsets();
            draw();
        });

        canvas.addEventListener('pointerdown', function (e) {
            if (!img) {
                return;
            }
            drag = { x: e.clientX - offsetX, y: e.clientY - offsetY };
            canvas.setPointerCapture(e.pointerId);
        });
        canvas.addEventListener('pointermove', function (e) {
            if (!drag || !img) {
                return;
            }
            offsetX = e.clientX - drag.x;
            offsetY = e.clientY - drag.y;
            clampOffsets();
            draw();
        });
        canvas.addEventListener('pointerup', function () { drag = null; });

        cancelBtn.addEventListener('click', function () {
            ui.hidden = true;
            fileInput.value = '';
            img = null;
        });

        applyBtn.addEventListener('click', function () {
            if (!img) {
                return;
            }
            var outWidth = parseInt(form.dataset.outWidth || '1024', 10);
            var outHeight = Math.round(outWidth / aspect);
            var out = document.createElement('canvas');
            out.width = outWidth;
            out.height = outHeight;
            var factor = outWidth / canvas.width;
            out.getContext('2d').drawImage(
                img, offsetX * factor, offsetY * factor, img.width * scale * factor, img.height * scale * factor);

            out.toBlob(function (blob) {
                var data = new FormData();
                data.append('kind', form.dataset.kind);
                data.append('file', blob, 'crop.webp');
                var token = form.querySelector('input[name=__RequestVerificationToken]');
                if (token) {
                    data.append('__RequestVerificationToken', token.value);
                }
                applyBtn.disabled = true;
                fetch(form.action, { method: 'POST', body: data })
                    .then(function (res) {
                        if (res.ok || res.redirected) {
                            location.reload();
                        } else {
                            applyBtn.disabled = false;
                            alert('Upload failed — check the file and try again.');
                        }
                    })
                    .catch(function () {
                        applyBtn.disabled = false;
                        alert('Upload failed — check your connection and try again.');
                    });
            }, 'image/webp', 0.9);
        });
    }

    function initAll() {
        document.querySelectorAll('form[data-media-crop]').forEach(init);
    }

    document.addEventListener('DOMContentLoaded', initAll);

    // Blazor enhanced navigation swaps the DOM without re-running scripts.
    if (window.Blazor && window.Blazor.addEventListener) {
        window.Blazor.addEventListener('enhancedload', initAll);
    } else {
        document.addEventListener('DOMContentLoaded', function () {
            if (window.Blazor && window.Blazor.addEventListener) {
                window.Blazor.addEventListener('enhancedload', initAll);
            }
        });
    }
})();
