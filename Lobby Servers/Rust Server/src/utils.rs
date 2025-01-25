use rand::Rng;

pub fn generate_private_key() -> String {
    let mut rng = rand::thread_rng();
    let random_bytes: Vec<u8> = (0..16).map(|_| rng.gen::<u8>()).collect();
    let private_key: String = random_bytes.iter().map(|b| format!("{:02x}", b)).collect();
    private_key
}
