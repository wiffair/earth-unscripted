import release from "../data/beta.json";
import { createHandler } from "./routes.mjs";

const fetch = createHandler(release);
export default { fetch } satisfies ExportedHandler<Env>;
