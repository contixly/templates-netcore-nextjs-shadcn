import OpenGraphImage, {
  alt,
  contentType,
  size,
} from "@/src/app/opengraph-image";

export { alt, contentType, size };

export default function TwitterImage() {
  return OpenGraphImage();
}
