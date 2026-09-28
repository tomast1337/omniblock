import unittest
import json
import tempfile
from pathlib import Path
from PIL import Image, ImageDraw
from check_block_gallery import load_run, measure


class GalleryDiffTests(unittest.TestCase):
    def test_identical_image_has_no_error(self):
        image = Image.new("RGB", (160, 120), (110, 120, 130))
        metrics, _ = measure(image, image.copy())
        self.assertEqual(metrics["meanRgbError"], 0)
        self.assertEqual(metrics["worst32pxChangedFraction"], 0)

    def test_single_missing_block_is_not_diluted_by_background(self):
        empty = Image.new("RGB", (850, 480), (180, 200, 230))
        block = empty.copy()
        ImageDraw.Draw(block).rectangle((320, 160, 350, 190), fill=(60, 70, 80))
        metrics, _ = measure(block, empty)
        self.assertLess(metrics["changedFraction"], .02)
        self.assertGreater(metrics["worst32pxChangedFraction"], .45)

    def test_small_rgb_noise_is_allowed(self):
        metrics, _ = measure(Image.new("RGB", (64, 64), (100, 100, 100)),
                             Image.new("RGB", (64, 64), (102, 99, 101)))
        self.assertLess(metrics["meanRgbError"], 3)
        self.assertEqual(metrics["changedFraction"], 0)

    def test_different_dimensions_fail(self):
        with self.assertRaises(ValueError):
            measure(Image.new("RGB", (10, 10)), Image.new("RGB", (11, 10)))

    def test_missing_capture_is_not_a_visual_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "result.json").write_text(json.dumps({"exitCode": 0}))
            (root / "block-gallery-000.json").write_text(json.dumps({"pages": 1}))
            with self.assertRaisesRegex(ValueError, "Incomplete"):
                load_run(root)

    def test_failed_game_is_rejected_even_before_comparison(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "result.json").write_text(json.dumps({"exitCode": 1}))
            with self.assertRaisesRegex(ValueError, "Unsuccessful"):
                load_run(root)


if __name__ == "__main__":
    unittest.main()
