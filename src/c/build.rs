fn main() {
    match std::env::var("CARGO_CFG_TARGET_OS").as_deref() {
        Ok("macos") => println!(
            "cargo:rustc-cdylib-link-arg=-Wl,-install_name,@rpath/libcloudtoid_interprocess.dylib"
        ),
        Ok("linux") => {
            println!("cargo:rustc-cdylib-link-arg=-Wl,-soname,libcloudtoid_interprocess.so")
        }
        _ => {}
    }
}
